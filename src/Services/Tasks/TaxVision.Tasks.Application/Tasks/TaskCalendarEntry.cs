using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks;

/// <summary>
/// La misma tarea que devuelve el tablero, vista por fecha. Sale de la misma tabla y del mismo
/// agregado: una tarea con vencimiento no es una cita ni una segunda entidad.
/// </summary>
public sealed record TaskCalendarEntry(
    Guid Id,
    string Title,
    DateTime DueAtUtc,
    string TimeZoneId,
    bool IsStatutory,
    TaskItemStatus Status,
    TaskPriority Priority,
    Guid? AssigneeUserId,
    Guid? CustomerId,
    bool IsBlocked
)
{
    /// <summary>Sólo entran las que tienen vencimiento: sin fecha no hay dónde pintarla.</summary>
    public static TaskCalendarEntry From(TaskItem task) =>
        new(
            task.Id,
            task.Title.Value,
            task.Due!.DueAtUtc,
            task.Due.TimeZoneId,
            task.Due.IsStatutory,
            task.Status,
            task.Priority,
            task.AssigneeUserId,
            task.Reference.CustomerId,
            task.IsBlocked
        );
}
