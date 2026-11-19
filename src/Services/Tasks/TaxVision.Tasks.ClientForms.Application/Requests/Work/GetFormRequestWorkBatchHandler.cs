using BuildingBlocks.Contracts.Work;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Work;

/// <summary>Lo pide <c>/tasks/work/summary</c> después de autorizar (<c>users.work.view</c>) y acotar la lista.</summary>
public sealed record GetFormRequestWorkBatchQuery(
    Guid TenantId,
    Guid ActorUserId,
    IReadOnlyList<Guid> UserIds,
    bool CanViewAll,
    DateTime NowUtc
);

public static class GetFormRequestWorkBatchHandler
{
    public static async Task<IReadOnlyList<WorkBatchRow>> Handle(
        GetFormRequestWorkBatchQuery query,
        IFormRequestWorkReader reader,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        if (query.UserIds.Count == 0)
            return [];

        var today = CivilDate.Of(query.NowUtc, await zones.ResolveAsync(query.TenantId, ct));
        return await reader.CountByUserAsync(
            query.TenantId,
            query.UserIds,
            query.ActorUserId,
            query.CanViewAll,
            today,
            ct
        );
    }
}
