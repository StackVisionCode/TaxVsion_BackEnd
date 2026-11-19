using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Work;

/// <param name="CanViewOthers">El actor tiene <c>users.work.view</c>.</param>
/// <param name="CanViewAll">El actor tiene <c>customers.view_all</c>.</param>
public sealed record GetUserWorkQuery(
    Guid TenantId,
    Guid ActorUserId,
    Guid UserId,
    bool CanViewOthers,
    bool CanViewAll,
    DateTime NowUtc
);

public sealed record TaskWork(IReadOnlyList<WorkCount> Counts, IReadOnlyList<WorkItem> Top);

/// <summary>Trabajo de una persona en Tasks, contado con la visibilidad de quien mira.</summary>
public static class GetUserWorkHandler
{
    public static async Task<Result<TaskWork>> Handle(
        GetUserWorkQuery query,
        ITaskWorkReader reader,
        IOfficeTimeZone zones,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (!WorkAccess.CanView(query.ActorUserId, query.UserId, query.CanViewOthers))
            return Result.Failure<TaskWork>(WorkErrors.Forbidden);

        var window = WorkWindow.For(query.NowUtc, await zones.ResolveAsync(query.TenantId, ct));
        var visibleTo = WorkAccess.VisibleTo(query.ActorUserId, query.CanViewAll, visibility.Value.Enabled);
        var counts = await reader.CountAsync(query.TenantId, query.UserId, visibleTo, window, ct);
        var top = await reader.TopAsync(query.TenantId, query.UserId, visibleTo, WorkLimits.TopItems, ct);
        return Result.Success(new TaskWork(counts, top));
    }
}
