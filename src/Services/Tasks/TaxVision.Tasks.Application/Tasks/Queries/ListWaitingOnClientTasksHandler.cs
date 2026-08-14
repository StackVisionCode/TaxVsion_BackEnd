using BuildingBlocks.Common;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record ListWaitingOnClientTasksQuery(Guid TenantId, int Page, int Size);

/// <summary>La pantalla de seguimiento: qué se le pidió a cada cliente y desde cuándo.</summary>
public static class ListWaitingOnClientTasksHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        ListWaitingOnClientTasksQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var page = await tasks.ListWaitingOnClientAsync(query.TenantId, query.Page, query.Size, ct);
        return TaskResponse.FromPage(page);
    }
}
