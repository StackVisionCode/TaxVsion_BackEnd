using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record GetTaskCalendarQuery(
    Guid TenantId,
    DateTime FromUtc,
    DateTime ToUtc,
    TaskQueryFilter Filter,
    int Take,
    Guid ActorUserId,
    bool CanViewAll,
    TaskDueBucket? Due = null,
    DateTime? NowUtc = null
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
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        if (query.ToUtc <= query.FromUtc || query.ToUtc - query.FromUtc > MaxRange)
            return Result.Failure<TaskCalendarResult>(RangeInvalid);

        var items = await LoadAsync(query, tasks, visibility, zones, ct);
        var entries = items.Take(query.Take).Select(TaskCalendarEntry.From).ToList();
        return Result.Success(new TaskCalendarResult(entries, items.Count > query.Take));
    }

    /// <summary>Uno de más que el tope, para saber si se cortó sin contar aparte.</summary>
    private static async Task<IReadOnlyList<TaskItem>> LoadAsync(
        GetTaskCalendarQuery query,
        ITaskRepository tasks,
        IOptions<TasksVisibilityOptions> visibility,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        // El mismo corte de vencimiento que el tablero, con el día civil de la oficina.
        var now = query.NowUtc ?? DateTime.UtcNow;
        var filter = await TaskDueFilters.ApplyAsync(query.TenantId, query.Filter, query.Due, now, zones, ct);
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        return await tasks.ListForCalendarAsync(
            query.TenantId,
            query.FromUtc,
            query.ToUtc,
            filter,
            query.Take + 1,
            assignedTo,
            ct
        );
    }
}
