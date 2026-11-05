using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// Lo que se anuncia cuando nace una tarea, venga del alta manual, de una plantilla o de una serie:
/// <c>TaskCreated</c>, su recordatorio de vencimiento y, si se pide, <c>TaskAssigned</c>. Se llama
/// antes de guardar, para que todo salga por el outbox en la misma transacción.
/// </summary>
public static class TaskBirthAnnouncements
{
    /// <param name="announceAssignment">
    /// Avisar al asignado. Notification ya descarta la autoasignación (asignado == quien asigna).
    /// </param>
    public static async Task PublishAsync(
        TaskItem task,
        IMessageBus bus,
        ICorrelationContext correlation,
        bool announceAssignment
    )
    {
        await bus.PublishAsync(TaskCreatedEventFactory.From(task, correlation.CorrelationId));
        await TaskDueReminder.PublishIfDueAsync(task, bus, correlation);

        if (announceAssignment && task.AssigneeUserId is { } assignee)
            await bus.PublishAsync(
                new TaskAssignedIntegrationEvent
                {
                    TenantId = task.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    TaskId = task.Id,
                    Title = task.Title.Value,
                    AssigneeUserId = assignee,
                    PreviousAssigneeUserId = null,
                    DueAtUtc = task.Due?.DueAtUtc,
                    AssignedByUserId = task.CreatedByUserId,
                }
            );
    }
}
