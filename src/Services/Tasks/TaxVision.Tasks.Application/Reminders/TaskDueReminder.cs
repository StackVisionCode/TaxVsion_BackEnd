using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Reminders;

/// <summary>
/// El recordatorio de vencimiento, armado igual para el alta, la asignación y la reapertura. Lo usan los
/// handlers de eventos de dominio, que corren después del commit: por eso decide con la tarea recargada.
/// <c>RequestKey</c> lo hace idempotente: reintentar no duplica el recordatorio.
/// </summary>
public static class TaskDueReminder
{
    /// <summary>Una hora antes: suficiente para reaccionar y no tanto como para olvidarlo de nuevo.</summary>
    private const int LeadMinutes = 60;

    /// <summary>
    /// Nada que pedir si no hay fecha, si la tarea ya se cerró o si <paramref name="assignee"/> ya no es el
    /// asignado: un evento posterior de la misma tarea pide el que corresponda.
    /// </summary>
    /// <param name="lifecycle">Ciclo de vida del evento que lo pide (la clave de Reminder).</param>
    public static ReminderRequestedIntegrationEvent? RequestFor(
        TaskItem task,
        Guid? assignee,
        int lifecycle,
        string correlationId
    )
    {
        if (task.Due is null || assignee is not { } userId || task.AssigneeUserId != userId)
            return null;
        if (task.Status is TaskItemStatus.Completed or TaskItemStatus.Cancelled)
            return null;

        return TaskReminderContracts.Requested(task, userId, LeadMinutes, correlationId, lifecycle);
    }
}
