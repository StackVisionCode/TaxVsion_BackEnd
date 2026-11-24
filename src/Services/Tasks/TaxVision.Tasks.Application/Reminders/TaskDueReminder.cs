using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

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

    /// <summary>Un vencimiento sin hora avisa a las 9:00 de la oficina del mismo día (decisión del dueño, 2026-11-14).</summary>
    private static readonly TimeOnly CivilDueReminderTime = new(9, 0);

    /// <summary>
    /// Nada que pedir si no hay fecha, si la tarea ya se cerró o si <paramref name="assignee"/> ya no es el
    /// asignado: un evento posterior de la misma tarea pide el que corresponda.
    /// </summary>
    /// <param name="lifecycle">Ciclo de vida del evento que lo pide (la clave de Reminder).</param>
    public static ReminderRequestedIntegrationEvent? RequestFor(
        TaskItem task,
        Guid? assignee,
        int lifecycle,
        string correlationId,
        string? cycleTag = null
    )
    {
        if (task.Due is null || assignee is not { } userId || task.AssigneeUserId != userId)
            return null;
        if (TaskItemStatuses.IsClosed(task.Status))
            return null;

        return TaskReminderContracts.Requested(task, userId, LeadFor(task.Due), correlationId, lifecycle, cycleTag);
    }

    /// <summary>Minutos entre el aviso y el vencimiento: 9:00 locales para fin del día, una hora para las demás.</summary>
    internal static int LeadFor(DueDate due)
    {
        if (!due.IsEndOfCivilDay)
            return LeadMinutes;

        var zone = ResolvedTimeZone.Resolve(due.TimeZoneId);
        var reminderUtc = WallTime
            .ToUtc(due.DueOn, CivilDueReminderTime, zone, NonexistentTimePolicy.ShiftForward)
            .Value;
        return (int)Math.Ceiling((due.DueAtUtc - reminderUtc).TotalMinutes);
    }
}
