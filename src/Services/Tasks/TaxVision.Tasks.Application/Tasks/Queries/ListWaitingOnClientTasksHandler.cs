using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record ListWaitingOnClientTasksQuery(
    Guid TenantId,
    int Page,
    int Size,
    Guid ActorUserId,
    bool CanViewAll
);

/// <summary>La pantalla de seguimiento: qué se le pidió a cada cliente y desde cuándo.</summary>
public static class ListWaitingOnClientTasksHandler
{
    public static async Task<PagedResult<TaskResponse>> Handle(
        ListWaitingOnClientTasksQuery query,
        ITaskRepository tasks,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var page = await tasks.ListWaitingOnClientAsync(query.TenantId, query.Page, query.Size, assignedTo, ct);
        return TaskResponse.FromPage(page);
    }
}
