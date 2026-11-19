using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Work;

/// <param name="CanViewOthers">El actor tiene <c>users.work.view</c>: el lote siempre lo exige.</param>
public sealed record GetWorkBatchQuery(
    Guid TenantId,
    Guid ActorUserId,
    IReadOnlyList<Guid> UserIds,
    bool CanViewOthers,
    bool CanViewAll,
    DateTime NowUtc
);

/// <summary>Abiertos y vencidos por persona para la tabla de miembros, en una consulta.</summary>
public static class GetWorkBatchHandler
{
    public static async Task<Result<IReadOnlyList<WorkBatchRow>>> Handle(
        GetWorkBatchQuery query,
        ITaskWorkReader reader,
        IOfficeTimeZone zones,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (!query.CanViewOthers)
            return Result.Failure<IReadOnlyList<WorkBatchRow>>(WorkErrors.Forbidden);

        var userIds = query.UserIds.Distinct().ToList();
        if (userIds.Count > WorkLimits.BatchUsers)
            return Result.Failure<IReadOnlyList<WorkBatchRow>>(WorkErrors.TooManyUsers);
        if (userIds.Count == 0)
            return Result.Success<IReadOnlyList<WorkBatchRow>>([]);

        var window = WorkWindow.For(query.NowUtc, await zones.ResolveAsync(query.TenantId, ct));
        var visibleTo = WorkAccess.VisibleTo(query.ActorUserId, query.CanViewAll, visibility.Value.Enabled);
        return Result.Success(await reader.CountByUserAsync(query.TenantId, userIds, visibleTo, window, ct));
    }
}
