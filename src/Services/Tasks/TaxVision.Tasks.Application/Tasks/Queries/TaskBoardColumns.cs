using TaxVision.Tasks.Application.Statuses;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Queries;

/// <summary>
/// Reglas del tablero por columnas: qué columnas hay (los estados activos salvo Cancelled, en el orden de la
/// oficina), qué tareas caen en cada una y cómo se pagina.
/// </summary>
public static class TaskBoardColumns
{
    public const int MaxTake = 50;

    private static readonly TaskItemStatus[] Stages =
    [
        TaskItemStatus.NotStarted,
        TaskItemStatus.InProgress,
        TaskItemStatus.WaitingOnClient,
        TaskItemStatus.Completed,
    ];

    public static async Task<IReadOnlyList<WorkflowStatusResponse>> ListAsync(
        IWorkflowStatusRepository statuses,
        Guid tenantId,
        CancellationToken ct
    ) =>
        [
            .. WorkflowStatusCatalog
                .Compose(await statuses.ListAsync(tenantId, false, ct))
                .Where(s => !s.IsArchived && Stages.Contains(s.Category))
                .OrderBy(s => Array.IndexOf(Stages, s.Category))
                .ThenBy(s => s.SortOrder),
        ];

    /// <summary>El por defecto de la etapa recoge lo que no tiene estado propio activo.</summary>
    public static TaskStatusCodeFilter FilterFor(
        WorkflowStatusResponse column,
        IReadOnlyList<WorkflowStatusResponse> all
    ) =>
        column.IsDefault
            ? new(
                column.Code,
                column.Category,
                true,
                [.. all.Where(s => s.Category == column.Category && s.Code != column.Code).Select(s => s.Code)]
            )
            : new(column.Code, column.Category, false);

    public static int TotalOf(
        WorkflowStatusResponse column,
        IReadOnlyList<WorkflowStatusResponse> all,
        IReadOnlyList<TaskBoardTally> tallies
    ) =>
        tallies
            .Where(t => t.Category == column.Category)
            .Where(t =>
                t.Code == column.Code
                || (column.IsDefault && !all.Any(s => s.Category == t.Category && s.Code == t.Code))
            )
            .Sum(t => t.Count);

    public static TaskBoardSort SortFor(WorkflowStatusResponse column, TaskBoardSort requested) =>
        column.Category == TaskItemStatus.Completed ? TaskBoardSort.RecentlyCompleted : requested;

    public static int Take(int requested) => Math.Clamp(requested <= 0 ? 20 : requested, 1, MaxTake);

    /// <summary>Se pide una de más: si llega, hay "Load more" desde el final de esta página.</summary>
    public static (IReadOnlyList<TaskCardResponse> Items, string? NextCursor) Page(
        IReadOnlyList<TaskCardResponse> rows,
        int skip,
        int take
    ) => rows.Count > take ? ([.. rows.Take(take)], (skip + take).ToString()) : (rows, null);

    public static async Task<TaskBoardColumnSlice> SliceAsync(
        ITaskBoardReader reader,
        Guid tenantId,
        TaskQueryFilter filter,
        WorkflowStatusResponse column,
        IReadOnlyList<WorkflowStatusResponse> all,
        int total,
        (int Skip, int Take, TaskBoardSort Sort) page,
        Guid? assignedTo,
        CancellationToken ct
    )
    {
        var rows = await reader.ListCardsAsync(
            tenantId,
            filter with
            {
                StatusCode = FilterFor(column, all),
            },
            SortFor(column, page.Sort),
            page.Skip,
            page.Take + 1,
            assignedTo,
            ct
        );
        var (items, next) = Page(rows, page.Skip, page.Take);
        return new TaskBoardColumnSlice(column.Code, column.Category, total, items, next);
    }
}
