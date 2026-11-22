using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;

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
    DateTime NowUtc = default
);

public static class SearchTasksHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        SearchTasksQuery query,
        ITaskRepository tasks,
        IOfficeTimeZone zones,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var filter = await WithDueAsync(query, zones, ct);
        var page = await tasks.SearchAsync(query.TenantId, filter, query.Page, query.Size, assignedTo, ct);
        return TaskResponse.FromPage(page);
    }

    // Mismo día civil que GET /tasks/summary: el total de la lista es el número del widget.
    private static async Task<TaskQueryFilter> WithDueAsync(
        SearchTasksQuery query,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        if (query.Due is not { } bucket)
            return query.Filter;
        var window = TaskSummaryWindow.For(query.NowUtc, await zones.ResolveAsync(query.TenantId, ct));
        return query.Filter with { Due = new TaskDueFilter(bucket, window) };
    }
}
