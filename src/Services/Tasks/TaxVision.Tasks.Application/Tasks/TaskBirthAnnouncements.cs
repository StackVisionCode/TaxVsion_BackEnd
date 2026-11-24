using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Tasks.Events;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// Lo que se anuncia cuando nace una tarea, venga del alta manual, de una subtarea, de una plantilla o de una
/// serie: <c>TaskCreated</c>, su recordatorio de vencimiento y <c>TaskAssigned</c> si es raíz. Cada ocurrencia de una
/// serie avisa a su responsable (D-28, en lugar de D-TSK-1).
/// </summary>
public static class TaskBirthAnnouncements
{
    public static OutgoingMessages For(TaskItem task, TaskCreatedDomainEvent born, string correlationId)
    {
        // Una ocurrencia de serie la crea el sistema: sin actor.
        var createdBy = born.RecurringOccurrence ? (Guid?)null : born.CreatedByUserId;
        var messages = new OutgoingMessages { TaskCreatedEventFactory.From(task, correlationId, createdBy) };

        if (TaskDueReminder.RequestFor(task, born.AssigneeUserId, lifecycle: 0, correlationId) is { } reminder)
            messages.Add(reminder);

        // Notification ya descarta la autoasignación (asignado == quien asigna).
        if (task.ParentTaskId is null && born.AssigneeUserId is { } assignee)
            messages.Add(
                new TaskAssignedIntegrationEvent
                {
                    TenantId = task.TenantId,
                    CorrelationId = correlationId,
                    TaskId = task.Id,
                    Title = task.Title.Value,
                    AssigneeUserId = assignee,
                    PreviousAssigneeUserId = null,
                    DueAtUtc = task.Due?.DueAtUtc,
                    DueOn = task.Due?.DueOn,
                    DueTimeZoneId = task.Due?.TimeZoneId,
                    AssignedByUserId = born.CreatedByUserId,
                }
            );

        return messages;
    }
}
