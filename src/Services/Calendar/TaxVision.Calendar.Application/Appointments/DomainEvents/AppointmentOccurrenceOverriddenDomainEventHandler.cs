using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using TaxVision.Calendar.Domain.Scheduling;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>Se movió una ocurrencia. Conserva su id (inicio original): basta con mover el ancla del aviso.</summary>
public static class AppointmentOccurrenceOverriddenDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        AppointmentOccurrenceOverriddenDomainEvent overridden,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        var found = await appointments.GetByIdAsync(overridden.TenantId, overridden.AppointmentId, ct);
        if (found.IsFailure)
            return messages;

        var appointment = found.Value;
        var correlationId = envelope.CorrelationId ?? string.Empty;
        var original = overridden.OriginalStartUtc;
        var moved = overridden.NewStartUtc ?? original;

        messages.Add(
            AppointmentEvents.Rescheduled(
                appointment,
                original,
                moved,
                overridden.NewEndUtc ?? moved.Add(appointment.Timing.Duration ?? TimeSpan.FromHours(1)),
                appointment.Timing.TimeZone.Id,
                nameof(EditScope.ThisOccurrence),
                original,
                correlationId
            )
        );
        messages.Add(
            new ReminderTargetMovedIntegrationEvent
            {
                TenantId = overridden.TenantId,
                CorrelationId = correlationId,
                Category = AppointmentReminderRequests.Category,
                TargetId = OccurrenceTargetId.For(overridden.AppointmentId, original),
                NewAnchorAtUtc = moved,
            }
        );
        return messages;
    }
}
