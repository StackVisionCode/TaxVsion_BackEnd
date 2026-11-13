using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;

namespace TaxVision.Calendar.Application.Appointments;

/// <summary>Traduce el agregado a los contratos que salen del servicio.</summary>
internal static class AppointmentEvents
{
    /// <summary>
    /// Los asistentes con correo, con su usuario al lado cuando lo tienen. Un asistente sin correo no
    /// sale: no hay a dónde escribirle.
    /// </summary>
    public static IReadOnlyList<AppointmentRecipient> RecipientsOf(
        Appointment appointment,
        IReadOnlyList<AttendeeRsvpToken>? rsvpTokens = null
    )
    {
        var recipients = new List<AppointmentRecipient>();
        foreach (var attendee in appointment.Attendees)
        {
            // El visitante de una demo pública recibe los correos de la demo (con su enlace de gestión), no estos.
            if (appointment.BookingPageId is not null && attendee.Kind == AttendeeKind.External)
                continue;
            if (attendee.Snapshot.Email is { } email)
                recipients.Add(
                    new AppointmentRecipient(
                        email,
                        attendee.UserId,
                        attendee.CustomerId,
                        attendee.Snapshot.DisplayName,
                        // Quien ya respondió (el organizador, el cliente que reservó) no recibe botones.
                        attendee.Response == AttendeeResponse.NeedsAction
                            ? TokenOf(rsvpTokens, attendee.Id)
                            : null
                    )
                );
        }

        return recipients;
    }

    /// <summary>El enlace en claro solo lo trae el evento de la operación que lo emitió.</summary>
    private static string? TokenOf(IReadOnlyList<AttendeeRsvpToken>? rsvpTokens, Guid attendeeId)
    {
        foreach (var token in rsvpTokens ?? [])
        {
            if (token.AttendeeId == attendeeId)
                return token.Token;
        }

        return null;
    }

    public static AppointmentScheduledIntegrationEvent Scheduled(
        Appointment appointment,
        string correlationId,
        IReadOnlyList<AttendeeRsvpToken>? rsvpTokens = null,
        string? bookedByCustomerName = null,
        bool approvedRequest = false
    )
    {
        var userIds = new List<Guid>();
        foreach (var attendee in appointment.Attendees)
        {
            if (attendee.UserId is { } userId)
                userIds.Add(userId);
        }

        var (start, end) = FirstWindowOf(appointment);
        return new AppointmentScheduledIntegrationEvent
        {
            TenantId = appointment.TenantId,
            CorrelationId = correlationId,
            AppointmentId = appointment.Id,
            Title = appointment.Title.Value,
            OrganizerUserId = appointment.OrganizerUserId,
            StartUtc = start,
            EndUtc = end,
            TimeZoneId = appointment.Timing.TimeZone.Id,
            IsRecurring = appointment.IsRecurring,
            IsVirtual = appointment.IsVirtual,
            CustomerId = appointment.CustomerId,
            TaxYear = appointment.TaxYear,
            AttendeeUserIds = userIds,
            Recipients = RecipientsOf(appointment, rsvpTokens),
            BookedByCustomerName = bookedByCustomerName,
            ApprovedRequest = approvedRequest,
        };
    }

    /// <summary>Inicio de la cita o de la primera ocurrencia de la serie; null si la regla no da ninguna.</summary>
    public static DateTime? FirstStartOf(Appointment appointment) =>
        appointment.Timing.StartUtc
        ?? (appointment.Recurrence is null ? null : OccurrenceExpander.FirstStart(appointment));

    /// <summary>
    /// Inicio y fin de la primera ocurrencia: una serie guarda hora de pared, no UTC. Sin ocurrencia
    /// calculable queda la hora actual, como antes.
    /// </summary>
    private static (DateTime StartUtc, DateTime EndUtc) FirstWindowOf(Appointment appointment)
    {
        var timing = appointment.Timing;
        if (timing.StartUtc is { } startUtc && timing.EndUtc is { } endUtc)
            return (startUtc, endUtc);

        if (
            appointment.Recurrence is not null
            && timing.Duration is { } duration
            && OccurrenceExpander.FirstStart(appointment) is { } firstUtc
        )
            return (firstUtc, firstUtc + duration);

        var nowUtc = DateTime.UtcNow;
        return (nowUtc, nowUtc);
    }

    /// <summary>
    /// Mover la cita sin avisar a los asistentes los deja presentándose a la hora vieja. Reminder se
    /// entera aparte, por objetivo cerrado y pedido nuevo.
    /// </summary>
    public static AppointmentRescheduledIntegrationEvent Rescheduled(
        Appointment appointment,
        DateTime? previousStartUtc,
        DateTime newStartUtc,
        DateTime newEndUtc,
        string timeZoneId,
        string scope,
        DateTime? originalStartUtc,
        string correlationId,
        IReadOnlyList<AttendeeRsvpToken>? rsvpTokens = null
    ) =>
        new()
        {
            TenantId = appointment.TenantId,
            CorrelationId = correlationId,
            AppointmentId = appointment.Id,
            Scope = scope,
            OriginalStartUtc = originalStartUtc,
            PreviousStartUtc = previousStartUtc,
            NewStartUtc = newStartUtc,
            NewEndUtc = newEndUtc,
            TimeZoneId = timeZoneId,
            MeetingId = appointment.MeetingId,
            OrganizerUserId = appointment.OrganizerUserId,
            Recipients = RecipientsOf(appointment, rsvpTokens),
        };

    public static AppointmentCancelledIntegrationEvent Cancelled(
        Appointment appointment,
        string scope,
        DateTime? originalStartUtc,
        string? reason,
        string correlationId
    ) =>
        new()
        {
            TenantId = appointment.TenantId,
            CorrelationId = correlationId,
            AppointmentId = appointment.Id,
            Scope = scope,
            OriginalStartUtc = originalStartUtc,
            Reason = reason,
            MeetingId = appointment.MeetingId,
            OrganizerUserId = appointment.OrganizerUserId,
            Recipients = RecipientsOf(appointment),
        };
}
