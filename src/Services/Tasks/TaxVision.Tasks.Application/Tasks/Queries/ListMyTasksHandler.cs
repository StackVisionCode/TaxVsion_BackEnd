using BuildingBlocks.Common;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record ListMyTasksQuery(Guid TenantId, Guid UserId, TaskItemStatus? Status, int Page, int Size);

/// <summary>
/// El usuario viaja dentro del predicado SQL, no como filtro posterior: filtrar en memoria después de
/// paginar rompería el total y dejaría páginas cortas.
/// </summary>
public static class ListMyTasksHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        ListMyTasksQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var page = await tasks.ListForAssigneeAsync(
            query.TenantId,
            query.UserId,
            query.Status,
            query.Page,
            query.Size,
            ct
        );
        return TaskResponse.FromPage(page);
    }
}
