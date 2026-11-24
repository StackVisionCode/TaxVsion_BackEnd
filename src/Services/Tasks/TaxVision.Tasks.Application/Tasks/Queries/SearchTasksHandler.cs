using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Statuses;
using TaxVision.Tasks.Application.Statuses.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Queries;

/// <param name="Due">Vencimiento del dashboard; se corta con el día civil de la oficina en <paramref name="NowUtc"/>.</param>
public sealed record SearchTasksQuery(
    Guid TenantId,
    TaskQueryFilter Filter,
    int Page,
    int Size,
    Guid ActorUserId,
    bool CanViewAll,
    TaskDueBucket? Due = null,
    DateTime NowUtc = default,
    string? StatusCode = null
);

public static class SearchTasksHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        SearchTasksQuery query,
        ITaskRepository tasks,
        IWorkflowStatusRepository statuses,
        IOfficeTimeZone zones,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var dated = await TaskDueFilters.ApplyAsync(query.TenantId, query.Filter, query.Due, query.NowUtc, zones, ct);
        var filter = await WithStatusCodeAsync(query, dated, statuses, ct);
        var page = await tasks.SearchAsync(query.TenantId, filter, query.Page, query.Size, assignedTo, ct);
        return await WithParentTitlesAsync(query.TenantId, page, tasks, ct);
    }

    /// <summary>Una subtarea en la lista se lee con su madre delante ("1040 — Ana López › Collect W-2").</summary>
    private static async Task<PagedResult<TaskResponse>> WithParentTitlesAsync(
        Guid tenantId,
        PagedResult<TaskItem> page,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var parentIds = page
            .Items.Where(t => t.ParentTaskId is not null)
            .Select(t => t.ParentTaskId!.Value)
            .ToHashSet();
        if (parentIds.Count == 0)
            return TaskResponse.FromPage(page);

        var titles = (await tasks.ListByIdsAsync(tenantId, parentIds, ct)).ToDictionary(t => t.Id, t => t.Title.Value);
        var items = page
            .Items.Select(t =>
                TaskResponse.From(t) with
                {
                    ParentTitle = t.ParentTaskId is { } parent ? titles.GetValueOrDefault(parent) : null,
                }
            )
            .ToList();
        return new PagedResult<TaskResponse>(items, page.Page, page.Size, page.TotalCount);
    }

    // Un código desconocido no encuentra nada, igual que un estado de otra oficina.
    private static async Task<TaskQueryFilter> WithStatusCodeAsync(
        SearchTasksQuery query,
        TaskQueryFilter filter,
        IWorkflowStatusRepository statuses,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(query.StatusCode))
            return filter;

        var catalog = WorkflowStatusCatalog.Compose(await statuses.ListAsync(query.TenantId, true, ct));
        var match = catalog.FirstOrDefault(s => s.Code == query.StatusCode.Trim().ToLowerInvariant());
        if (match is null)
            return filter with
            {
                StatusCode = new TaskStatusCodeFilter(query.StatusCode, TaskItemStatus.NotStarted, false),
            };
        // Como el tablero: el por defecto recoge también las tareas con un estado archivado de su etapa.
        return filter with
        {
            StatusCode = match.IsDefault
                ? TaskBoardColumns.FilterFor(match, [.. catalog.Where(s => !s.IsArchived)])
                : new TaskStatusCodeFilter(match.Code, match.Category, false),
        };
    }
}
