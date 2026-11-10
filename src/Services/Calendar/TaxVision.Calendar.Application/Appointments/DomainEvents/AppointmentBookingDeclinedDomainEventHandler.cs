using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>Rechazada: el aviso va solo a quien la pidió (sin usuario del equipo), con el motivo.</summary>
public static class AppointmentBookingDeclinedDomainEventHandler
{
    public static async Task<AppointmentBookingDeclinedIntegrationEvent?> Handle(
        AppointmentBookingDeclinedDomainEvent declined,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(declined.TenantId, declined.AppointmentId, ct);
        if (found.IsFailure)
            return null;

        var appointment = found.Value;
        var recipients = new List<AppointmentRecipient>();
        foreach (var attendee in appointment.Attendees)
        {
            if (attendee.UserId is null && attendee.Snapshot.Email is { } email)
                recipients.Add(
                    new AppointmentRecipient(email, null, attendee.CustomerId, attendee.Snapshot.DisplayName)
                );
        }

        return new AppointmentBookingDeclinedIntegrationEvent
        {
            TenantId = declined.TenantId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            AppointmentId = appointment.Id,
            Title = appointment.Title.Value,
            StartUtc = AppointmentEvents.FirstStartOf(appointment),
            TimeZoneId = appointment.Timing.TimeZone.Id,
            Reason = declined.Reason,
            CustomerId = appointment.CustomerId,
            Recipients = recipients,
        };
    }
}
