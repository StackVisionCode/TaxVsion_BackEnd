using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks.Events;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.DomainEvents;

/// <summary>
/// Se completó la tarea: <c>TaskCompleted</c> y el cierre del recordatorio. El cierre sale aunque la tarea ya
/// no exista; sin él, el recordatorio de una tarea hecha llega igual.
/// </summary>
public static class TaskCompletedDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        TaskCompletedDomainEvent completed,
        ITaskRepository tasks,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        var found = await tasks.GetByIdAsync(completed.TenantId, completed.TaskId, ct);
        if (found.IsSuccess)
            messages.Add(
                new TaskCompletedIntegrationEvent
                {
                    TenantId = completed.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    TaskId = completed.TaskId,
                    Title = found.Value.Title.Value,
                    CompletedByUserId = completed.CompletedByUserId,
                    CompletedAtUtc = completed.CompletedAtUtc,
                    CustomerId = found.Value.Reference.CustomerId,
                    TaxYear = found.Value.Reference.TaxYear,
                }
            );

        messages.Add(
            TaskReminderContracts.Closed(
                completed.TenantId,
                completed.TaskId,
                TaskReminderContracts.ClosureReasons.Completed,
                correlation.CorrelationId
            )
        );
        return messages;
    }
}
