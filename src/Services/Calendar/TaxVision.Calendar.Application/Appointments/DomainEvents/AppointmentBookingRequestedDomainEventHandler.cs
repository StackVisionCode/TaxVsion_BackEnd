using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>Un cliente pidió una cita: la oficina se entera para aprobarla, asignarla o rechazarla.</summary>
public static class AppointmentBookingRequestedDomainEventHandler
{
    public static async Task<AppointmentBookingRequestedIntegrationEvent?> Handle(
        AppointmentBookingRequestedDomainEvent requested,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(requested.TenantId, requested.AppointmentId, ct);
        if (found.IsFailure)
            return null;

        var appointment = found.Value;
        return new AppointmentBookingRequestedIntegrationEvent
        {
            TenantId = requested.TenantId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            AppointmentId = appointment.Id,
            Title = appointment.Title.Value,
            StartUtc = AppointmentEvents.FirstStartOf(appointment),
            TimeZoneId = appointment.Timing.TimeZone.Id,
            OrganizerUserId = requested.OrganizerUserId,
            CustomerId = appointment.CustomerId,
            CustomerName = requested.CustomerName,
        };
    }
}
