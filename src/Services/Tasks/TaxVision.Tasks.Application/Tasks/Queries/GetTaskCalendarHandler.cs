using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record GetTaskCalendarQuery(
    Guid TenantId,
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? AssigneeUserId,
    int Take,
    Guid ActorUserId,
    bool CanViewAll
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
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var items = await tasks.ListForCalendarAsync(
            query.TenantId,
            query.FromUtc,
            query.ToUtc,
            query.AssigneeUserId,
            query.Take,
            assignedTo,
            ct
        );
        return [.. items.Select(TaskCalendarEntry.From)];
    }
}
