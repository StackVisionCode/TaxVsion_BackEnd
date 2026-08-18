using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Appointments;

/// <summary>Traduce el agregado a los contratos que salen del servicio.</summary>
internal static class AppointmentEvents
{
    /// <summary>
    /// Los asistentes con correo, con su usuario al lado cuando lo tienen. Un asistente sin correo no
    /// sale: no hay a dónde escribirle.
    /// </summary>
    public static IReadOnlyList<AppointmentRecipient> RecipientsOf(Appointment appointment)
    {
        var recipients = new List<AppointmentRecipient>();
        foreach (var attendee in appointment.Attendees)
        {
            if (attendee.Snapshot.Email is { } email)
                recipients.Add(new AppointmentRecipient(email, attendee.UserId));
        }

        return recipients;
    }

    public static AppointmentScheduledIntegrationEvent Scheduled(Appointment appointment, string correlationId)
    {
        var userIds = new List<Guid>();
        foreach (var attendee in appointment.Attendees)
        {
            if (attendee.UserId is { } userId)
                userIds.Add(userId);
        }

        return new AppointmentScheduledIntegrationEvent
        {
            TenantId = appointment.TenantId,
            CorrelationId = correlationId,
            AppointmentId = appointment.Id,
            Title = appointment.Title.Value,
            OrganizerUserId = appointment.OrganizerUserId,
            StartUtc = appointment.Timing.StartUtc ?? DateTime.UtcNow,
            EndUtc = appointment.Timing.EndUtc ?? DateTime.UtcNow,
            TimeZoneId = appointment.Timing.TimeZone.Id,
            IsRecurring = appointment.IsRecurring,
            IsVirtual = appointment.IsVirtual,
            CustomerId = appointment.CustomerId,
            TaxYear = appointment.TaxYear,
            AttendeeUserIds = userIds,
            Recipients = RecipientsOf(appointment),
        };
    }
}
