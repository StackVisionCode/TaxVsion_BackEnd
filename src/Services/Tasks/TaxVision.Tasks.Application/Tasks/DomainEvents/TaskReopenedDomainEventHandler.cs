using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks.Events;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.DomainEvents;

/// <summary>
/// La tarea volvió a abrirse. El cierre canceló el recordatorio; la clave del ciclo nuevo lo vuelve a pedir
/// para el asignado que quedó (si estaba inactivo, la reapertura lo quitó).
/// </summary>
public static class TaskReopenedDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        TaskReopenedDomainEvent reopened,
        ITaskRepository tasks,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages
        {
            new TaskReopenedIntegrationEvent
            {
                TenantId = reopened.TenantId,
                CorrelationId = correlation.CorrelationId,
                TaskId = reopened.TaskId,
                ReopenedByUserId = reopened.ReopenedByUserId,
                ReopenedAtUtc = reopened.ReopenedAtUtc,
            },
        };

        var found = await tasks.GetByIdAsync(reopened.TenantId, reopened.TaskId, ct);
        var reminder = found.IsSuccess
            ? TaskDueReminder.RequestFor(found.Value, found.Value.AssigneeUserId, reopened.ReopenCount, correlation.CorrelationId)
            : null;
        if (reminder is not null)
            messages.Add(reminder);
        return messages;
    }
}
