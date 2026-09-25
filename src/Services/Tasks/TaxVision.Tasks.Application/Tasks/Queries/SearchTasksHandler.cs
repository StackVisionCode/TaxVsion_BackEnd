using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record SearchTasksQuery(
    Guid TenantId,
    TaskQueryFilter Filter,
    int Page,
    int Size,
    Guid ActorUserId,
    bool CanViewAll
);

public static class SearchTasksHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        SearchTasksQuery query,
        ITaskRepository tasks,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var page = await tasks.SearchAsync(query.TenantId, query.Filter, query.Page, query.Size, assignedTo, ct);
        return TaskResponse.FromPage(page);
    }
}
