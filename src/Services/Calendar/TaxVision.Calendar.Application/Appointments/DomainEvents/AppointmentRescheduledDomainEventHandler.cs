using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>La cita o la serie entera se movió. Los instantes salen del evento; los asistentes, de la cita.</summary>
public static class AppointmentRescheduledDomainEventHandler
{
    public static async Task<AppointmentRescheduledIntegrationEvent?> Handle(
        AppointmentRescheduledDomainEvent moved,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var found = await appointments.GetByIdAsync(moved.TenantId, moved.AppointmentId, ct);
        if (found.IsFailure)
            return null;

        var newStartUtc = moved.NewStartUtc ?? moved.MovedAtUtc;
        return AppointmentEvents.Rescheduled(
            found.Value,
            moved.PreviousStartUtc,
            newStartUtc,
            moved.NewEndUtc ?? newStartUtc,
            moved.TimeZoneId,
            nameof(EditScope.EntireSeries),
            null,
            envelope.CorrelationId ?? string.Empty,
            moved.RsvpTokens
        );
    }
}
