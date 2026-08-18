using BuildingBlocks.Common;
using TaxVision.Tasks.Domain.Tasks;
using Wolverine;

namespace TaxVision.Tasks.Application.Reminders;

/// <summary>
/// Pedir el recordatorio de vencimiento vive acá y no en cada handler porque hay dos caminos por los
/// que nace una tarea con fecha —el alta manual y la materialización de una serie— y los dos deben
/// pedirlo igual. <c>RequestKey</c> lo hace idempotente: reintentar no duplica el recordatorio.
/// </summary>
public static class TaskDueReminder
{
    /// <summary>Una hora antes: suficiente para reaccionar y no tanto como para olvidarlo de nuevo.</summary>
    private const int LeadMinutes = 60;

    /// <summary>
    /// Sin fecha no hay ancla y sin asignado no hay a quién avisarle; en cualquiera de los dos casos
    /// no hay recordatorio que pedir y no es un error.
    /// </summary>
    public static async Task PublishIfDueAsync(TaskItem task, IMessageBus bus, ICorrelationContext correlation)
    {
        if (task.Due is null || task.AssigneeUserId is not { } assignee)
            return;

        await bus.PublishAsync(TaskReminderContracts.Requested(task, assignee, LeadMinutes, correlation.CorrelationId));
    }
}
