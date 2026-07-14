using BuildingBlocks.Caching;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Entitlements.Queries;

public static class GetTenantEntitlementSummaryHandler
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public static async Task<Result<EntitlementSummaryResponse>> Handle(
        GetTenantEntitlementSummaryQuery query,
        ITenantEntitlementSnapshotRepository snapshots,
        ICacheService cache,
        CancellationToken ct
    )
    {
        var cacheKey = EntitlementCacheKeys.Summary(query.TenantId);

        var response = await cache.GetOrCreateAsync(
            cacheKey,
            async innerCt =>
            {
                var snapshot = await snapshots.GetByTenantIdAsync(query.TenantId, innerCt);
                return snapshot is null ? null : EntitlementSummaryMapper.ToResponse(snapshot);
            },
            CacheTtl,
            ct
        );

        return response is null
            ? Result.Failure<EntitlementSummaryResponse>(new Error("EntitlementSnapshot.NotFound", "No entitlement snapshot exists for this tenant yet."))
            : Result.Success(response);
    }
}
