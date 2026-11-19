using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Work;

/// <param name="CanViewOthers">El actor tiene <c>users.work.view</c>.</param>
/// <param name="CanViewAll">El actor tiene <c>customers.view_all</c>.</param>
public sealed record GetWorkItemsQuery(
    Guid TenantId,
    Guid ActorUserId,
    Guid UserId,
    bool CanViewOthers,
    bool CanViewAll,
    string? Kind,
    int Page,
    int Size
);

/// <summary>"Show all" de un bloque de Tasks: misma autorización y mismo universo que <see cref="GetUserWorkHandler"/>.</summary>
public static class GetWorkItemsHandler
{
    public static async Task<Result<PagedResult<WorkItem>>> Handle(
        GetWorkItemsQuery query,
        ITaskWorkReader reader,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (!WorkAccess.CanView(query.ActorUserId, query.UserId, query.CanViewOthers))
            return Result.Failure<PagedResult<WorkItem>>(WorkErrors.Forbidden);
        if (query.Kind is not (WorkKinds.Tasks or WorkKinds.ClientRequests))
            return Result.Failure<PagedResult<WorkItem>>(WorkErrors.InvalidKind);

        var visibleTo = WorkAccess.VisibleTo(query.ActorUserId, query.CanViewAll, visibility.Value.Enabled);
        var page = await reader.ItemsAsync(
            query.TenantId,
            query.UserId,
            visibleTo,
            query.Kind,
            query.Page,
            query.Size,
            ct
        );
        return Result.Success(page);
    }
}
