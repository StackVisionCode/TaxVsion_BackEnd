using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>Cambiaron los detalles. Qué cambió sale del evento; la cita, de la recarga.</summary>
public static class AppointmentDetailsChangedDomainEventHandler
{
    public static async Task<AppointmentDetailsChangedIntegrationEvent?> Handle(
        AppointmentDetailsChangedDomainEvent changed,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(changed.TenantId, changed.AppointmentId, ct);
        if (found.IsFailure)
            return null;

        var appointment = found.Value;
        return new AppointmentDetailsChangedIntegrationEvent
        {
            TenantId = changed.TenantId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            AppointmentId = appointment.Id,
            Title = appointment.Title.Value,
            PreviousTitle = changed.PreviousTitle,
            Description = appointment.Description,
            Location = appointment.Location?.Value,
            ChangedFields = changed.ChangedFields,
            StartUtc = AppointmentEvents.FirstStartOf(appointment),
            TimeZoneId = appointment.Timing.TimeZone.Id,
            IsRecurring = appointment.IsRecurring,
            MeetingId = appointment.MeetingId,
            OrganizerUserId = appointment.OrganizerUserId,
            ChangedByUserId = changed.ChangedByUserId,
            Recipients = AppointmentEvents.RecipientsOf(appointment),
        };
    }
}
