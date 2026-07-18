using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Entitlements;

/// <summary>
/// Reconstruye el <see cref="TenantEntitlementSnapshot"/> de un tenant a partir de su
/// estado actual: plan publicado + add-ons activos + seats. Determinista — el mismo
/// estado siempre produce el mismo conjunto de entries (salvo ComputedAtUtc/RevisionNumber).
/// No hay overrides administrativos todavía (fuera de alcance de esta fase).
/// </summary>
public static class EntitlementSnapshotBuilder
{
    public static async Task<Result<TenantEntitlementSnapshot>> BuildAsync(
        Guid tenantId,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        ISubscriptionSeatRepository seats,
        ITenantAddOnRepository tenantAddOns,
        IAddOnDefinitionRepository addOnDefinitions,
        ITenantEntitlementSnapshotRepository snapshots,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(tenantId, ct);
        if (subscription is null)
            return Result.Failure<TenantEntitlementSnapshot>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var planVersion = plan?.GetPublishedVersion();
        if (plan is null || planVersion is null)
            return Result.Failure<TenantEntitlementSnapshot>(
                new Error("Plan.NoPublishedVersion", "Plan has no published version.")
            );

        var entries = SeedEntriesFromPlan(planVersion);
        await MergeActiveAddOnsAsync(tenantId, tenantAddOns, addOnDefinitions, entries, ct);

        var (seatCount, availableSeatCount) = await CountSeatsAsync(tenantId, seats, ct);
        var previousRevision = (await snapshots.GetByTenantIdAsync(tenantId, ct))?.RevisionNumber ?? 0;

        return TenantEntitlementSnapshot.Rebuild(
            tenantId,
            previousRevision,
            plan.Code.Value,
            planVersion.Id,
            subscription.Status.ToString(),
            seatCount,
            availableSeatCount,
            entries.Values.ToList(),
            DateTime.UtcNow
        );
    }

    private static Dictionary<string, EntitlementEntry> SeedEntriesFromPlan(SubscriptionPlanVersion planVersion)
    {
        var entries = new Dictionary<string, EntitlementEntry>();

        foreach (var entitlement in planVersion.Entitlements)
        {
            entries[entitlement.Key.Value] = new EntitlementEntry(
                entitlement.Key,
                entitlement.ValueType,
                entitlement.DefaultValue,
                EntitlementStatus.Active,
                EntitlementSource.Plan,
                ExpiresAtUtc: null
            );
        }

        foreach (var feature in planVersion.Features)
        {
            entries[feature.FeatureKey.Value] = new EntitlementEntry(
                feature.FeatureKey,
                EntitlementValueType.Bool,
                feature.DefaultEnabled.ToString(),
                EntitlementStatus.Active,
                EntitlementSource.Plan,
                ExpiresAtUtc: null
            );
        }

        return entries;
    }

    private static async Task MergeActiveAddOnsAsync(
        Guid tenantId,
        ITenantAddOnRepository tenantAddOns,
        IAddOnDefinitionRepository addOnDefinitions,
        Dictionary<string, EntitlementEntry> entries,
        CancellationToken ct
    )
    {
        var addOns = await tenantAddOns.GetByTenantIdAsync(tenantId, ct);

        foreach (var addOn in addOns)
        {
            if (addOn.Status != AddOnStatus.Active)
                continue;

            var definition = await addOnDefinitions.GetByIdAsync(addOn.AddOnDefinitionId, ct);
            if (definition is null)
                continue;

            foreach (var feature in definition.Features)
            {
                if (!feature.Enabled)
                    continue;

                entries.TryGetValue(feature.FeatureKey.Value, out var existing);
                entries[feature.FeatureKey.Value] = EntitlementMerger.MergeAddOnValue(
                    existing,
                    feature.FeatureKey,
                    EntitlementValueType.Bool,
                    "true",
                    AddOnMergeStrategy.Or
                );
            }

            foreach (var entitlement in definition.Entitlements)
            {
                entries.TryGetValue(entitlement.Key.Value, out var existing);
                entries[entitlement.Key.Value] = EntitlementMerger.MergeAddOnValue(
                    existing,
                    entitlement.Key,
                    entitlement.ValueType,
                    entitlement.Value,
                    entitlement.MergeStrategy
                );
            }
        }
    }

    private static async Task<(int SeatCount, int AvailableSeatCount)> CountSeatsAsync(
        Guid tenantId,
        ISubscriptionSeatRepository seats,
        CancellationToken ct
    )
    {
        var tenantSeats = await seats.GetByTenantIdAsync(tenantId, ct);

        var seatCount = 0;
        var availableSeatCount = 0;
        foreach (var seat in tenantSeats)
        {
            if (seat.Status is SeatStatus.Cancelled or SeatStatus.Expired or SeatStatus.Released)
                continue;

            seatCount++;
            if (seat.Status == SeatStatus.Available)
                availableSeatCount++;
        }

        return (seatCount, availableSeatCount);
    }
}
