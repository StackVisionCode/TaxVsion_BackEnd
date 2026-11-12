using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record GetTaskCalendarQuery(
    Guid TenantId,
    DateTime FromUtc,
    DateTime ToUtc,
    TaskQueryFilter Filter,
    int Take,
    Guid ActorUserId,
    bool CanViewAll
);

/// <summary>Las tareas del rango y si se cortaron en <c>Take</c>.</summary>
public sealed record TaskCalendarResult(IReadOnlyList<TaskCalendarEntry> Entries, bool Truncated);

/// <summary>
/// Mismo repositorio, misma tabla y mismos filtros que el tablero. La única diferencia es la forma de salida: no
/// hay un segundo modelo de tarea para el calendario.
/// </summary>
public static class GetTaskCalendarHandler
{
    // Cubre la vista mensual (6 semanas) con margen.
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(62);

    public static readonly Error RangeInvalid = new(
        "Tasks.Calendar.RangeInvalid",
        "The range must start before it ends and span at most 62 days."
    );

    public static async Task<Result<TaskCalendarResult>> Handle(
        GetTaskCalendarQuery query,
        ITaskRepository tasks,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (query.ToUtc <= query.FromUtc || query.ToUtc - query.FromUtc > MaxRange)
            return Result.Failure<TaskCalendarResult>(RangeInvalid);

        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        // Uno de más para saber si había más que el tope sin contar aparte.
        var items = await tasks.ListForCalendarAsync(
            query.TenantId,
            query.FromUtc,
            query.ToUtc,
            query.Filter,
            query.Take + 1,
            assignedTo,
            ct
        );
        var entries = items.Take(query.Take).Select(TaskCalendarEntry.From).ToList();
        return Result.Success(new TaskCalendarResult(entries, items.Count > query.Take));
    }
}
