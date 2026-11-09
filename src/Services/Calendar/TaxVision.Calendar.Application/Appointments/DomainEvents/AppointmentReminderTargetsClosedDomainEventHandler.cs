using TaxVision.Calendar.Domain.Appointments.Events;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>Cierra en Reminder los objetivos del evento. No relee: la cita ya no los produce.</summary>
public static class AppointmentReminderTargetsClosedDomainEventHandler
{
    public static OutgoingMessages Handle(AppointmentReminderTargetsClosedDomainEvent closed, Envelope envelope)
    {
        var messages = new OutgoingMessages();
        messages.AddRange(
            AppointmentReminderTargets.Close(
                closed.TenantId,
                closed.TargetIds,
                envelope.CorrelationId ?? string.Empty,
                closed.Reason
            )
        );
        return messages;
    }
}
