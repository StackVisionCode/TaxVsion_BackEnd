using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>La cita se canceló: aviso a los asistentes. El cierre en Reminder va por su propio evento.</summary>
public static class AppointmentCancelledDomainEventHandler
{
    public static async Task<AppointmentCancelledIntegrationEvent?> Handle(
        AppointmentCancelledDomainEvent cancelled,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(cancelled.TenantId, cancelled.AppointmentId, ct);
        if (found.IsFailure)
            return null;

        return AppointmentEvents.Cancelled(
            found.Value,
            (cancelled.Scope ?? EditScope.EntireSeries).ToString(),
            cancelled.OriginalStartUtc,
            cancelled.Reason,
            envelope.CorrelationId ?? string.Empty
        );
    }
}
