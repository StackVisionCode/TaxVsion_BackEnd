using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks.Events;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.DomainEvents;

/// <summary>
/// Cambió el responsable: aviso al nuevo asignado y su recordatorio de vencimiento (es por persona; la clave
/// lo hace idempotente).
/// </summary>
public static class TaskAssignedDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        TaskAssignedDomainEvent assigned,
        ITaskRepository tasks,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        var found = await tasks.GetByIdAsync(assigned.TenantId, assigned.TaskId, ct);
        if (found.IsFailure)
            return messages;

        var task = found.Value;
        messages.Add(
            new TaskAssignedIntegrationEvent
            {
                TenantId = assigned.TenantId,
                CorrelationId = correlation.CorrelationId,
                TaskId = assigned.TaskId,
                Title = task.Title.Value,
                AssigneeUserId = assigned.AssigneeUserId,
                PreviousAssigneeUserId = assigned.PreviousAssigneeUserId,
                DueAtUtc = task.Due?.DueAtUtc,
                AssignedByUserId = assigned.AssignedByUserId,
            }
        );
        var reminder = TaskDueReminder.RequestFor(
            task,
            assigned.AssigneeUserId,
            assigned.ReopenCount,
            correlation.CorrelationId
        );
        if (reminder is not null)
            messages.Add(reminder);
        return messages;
    }
}
