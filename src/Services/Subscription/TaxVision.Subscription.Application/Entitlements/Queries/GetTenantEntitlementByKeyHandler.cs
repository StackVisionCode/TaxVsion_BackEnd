using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Entitlements.Queries;

public static class GetTenantEntitlementByKeyHandler
{
    public static async Task<Result<EntitlementValueResponse>> Handle(
        GetTenantEntitlementByKeyQuery query,
        ITenantEntitlementSnapshotRepository snapshots,
        CancellationToken ct
    )
    {
        var snapshot = await snapshots.GetByTenantIdAsync(query.TenantId, ct);
        if (snapshot is null)
            return Result.Failure<EntitlementValueResponse>(
                new Error("EntitlementSnapshot.NotFound", "No entitlement snapshot exists for this tenant yet.")
            );

        var entry = snapshot.FindByKey(query.Key);
        if (entry is null)
            return Result.Failure<EntitlementValueResponse>(
                new Error("Entitlement.NotFound", $"Entitlement '{query.Key}' does not exist for this tenant.")
            );

        return Result.Success(
            new EntitlementValueResponse(
                entry.ValueType.ToString(),
                entry.Value,
                entry.Status.ToString(),
                entry.Source.ToString(),
                entry.ExpiresAtUtc
            )
        );
    }
}
