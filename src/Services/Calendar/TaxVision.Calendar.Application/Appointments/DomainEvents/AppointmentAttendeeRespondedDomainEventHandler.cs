using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>Va al organizador: es quien decide si mueve la cita porque alguien no puede.</summary>
public static class AppointmentAttendeeRespondedDomainEventHandler
{
    public static AppointmentAttendeeRespondedIntegrationEvent Handle(
        AppointmentAttendeeRespondedDomainEvent responded,
        Envelope envelope
    ) =>
        new()
        {
            TenantId = responded.TenantId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            AppointmentId = responded.AppointmentId,
            OrganizerUserId = responded.OrganizerUserId,
            UserId = responded.UserId,
            Response = responded.Response.ToString(),
        };
}
