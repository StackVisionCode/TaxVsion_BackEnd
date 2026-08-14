using BuildingBlocks.Common;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record SearchTasksQuery(Guid TenantId, TaskQueryFilter Filter, int Page, int Size);

public static class SearchTasksHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        SearchTasksQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var page = await tasks.SearchAsync(query.TenantId, query.Filter, query.Page, query.Size, ct);
        return TaskResponse.FromPage(page);
    }
}
