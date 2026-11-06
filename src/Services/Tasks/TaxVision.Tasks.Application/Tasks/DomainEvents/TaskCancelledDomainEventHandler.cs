using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Domain.Tasks.Events;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.DomainEvents;

/// <summary>Se canceló la tarea: <c>TaskCancelled</c> y el cierre del recordatorio. Todo sale del evento.</summary>
public static class TaskCancelledDomainEventHandler
{
    public static OutgoingMessages Handle(TaskCancelledDomainEvent cancelled, ICorrelationContext correlation) =>
        new()
        {
            new TaskCancelledIntegrationEvent
            {
                TenantId = cancelled.TenantId,
                CorrelationId = correlation.CorrelationId,
                TaskId = cancelled.TaskId,
                Reason = cancelled.Reason,
                CancelledByUserId = cancelled.CancelledByUserId,
            },
            TaskReminderContracts.Closed(
                cancelled.TenantId,
                cancelled.TaskId,
                TaskReminderContracts.ClosureReasons.Cancelled,
                correlation.CorrelationId
            ),
        };
}
