using BuildingBlocks.Contracts.Work;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Work;

/// <summary>Lo pide <c>/tasks/work</c> después de autorizar al actor (propio o <c>users.work.view</c>).</summary>
public sealed record GetFormRequestWorkQuery(
    Guid TenantId,
    Guid ActorUserId,
    Guid UserId,
    bool CanViewAll,
    DateTime NowUtc
);

public static class GetFormRequestWorkHandler
{
    public static async Task<FormRequestWork> Handle(
        GetFormRequestWorkQuery query,
        IFormRequestWorkReader reader,
        IOfficeTimeZone zones,
        CancellationToken ct
    )
    {
        var today = CivilDate.Of(query.NowUtc, await zones.ResolveAsync(query.TenantId, ct));
        var count = await reader.CountAsync(
            query.TenantId,
            query.UserId,
            query.ActorUserId,
            query.CanViewAll,
            today,
            ct
        );
        var top = await reader.TopAsync(
            query.TenantId,
            query.UserId,
            query.ActorUserId,
            query.CanViewAll,
            WorkLimits.TopItems,
            ct
        );
        return new FormRequestWork(count, top);
    }
}
