using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Tasks.Events;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// Lo que se anuncia cuando nace una tarea, venga del alta manual, de una subtarea, de una plantilla o de una
/// serie: <c>TaskCreated</c>, su recordatorio de vencimiento y <c>TaskAssigned</c> si es raíz. Por D-TSK-1, una
/// plantilla avisa y una serie solo en su alta, no en cada ocurrencia que siembra después.
/// </summary>
public static class TaskBirthAnnouncements
{
    public static OutgoingMessages For(TaskItem task, TaskCreatedDomainEvent born, string correlationId)
    {
        var messages = new OutgoingMessages { TaskCreatedEventFactory.From(task, correlationId) };

        if (TaskDueReminder.RequestFor(task, born.AssigneeUserId, lifecycle: 0, correlationId) is { } reminder)
            messages.Add(reminder);

        // Notification ya descarta la autoasignación (asignado == quien asigna).
        if (task.ParentTaskId is null && !born.RecurringOccurrence && born.AssigneeUserId is { } assignee)
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
                    AssignedByUserId = born.CreatedByUserId,
                }
            );

        return messages;
    }
}
