using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record GetTaskCalendarQuery(
    Guid TenantId,
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? AssigneeUserId,
    int Take
);

/// <summary>
/// Mismo repositorio y misma tabla que el tablero. La única diferencia es la forma de salida: no hay
/// un segundo modelo de tarea para el calendario.
/// </summary>
public static class GetTaskCalendarHandler
{
    public static async Task<IReadOnlyList<TaskCalendarEntry>> Handle(
        GetTaskCalendarQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var items = await tasks.ListForCalendarAsync(
            query.TenantId,
            query.FromUtc,
            query.ToUtc,
            query.AssigneeUserId,
            query.Take,
            ct
        );
        return [.. items.Select(TaskCalendarEntry.From)];
    }
}
