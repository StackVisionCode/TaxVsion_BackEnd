using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Plans.Queries;

/// <summary>CAT-F2.2 — handler que materializa el aggregate completo para la UI de autoría.
/// Devuelve todas las versiones, no sólo la publicada.</summary>
public static class GetPlansForAdminHandler
{
    public static async Task<Result<IReadOnlyList<PlanAdminResponse>>> Handle(
        GetPlansForAdminQuery _,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var all = await plans.GetAllAsync(ct);
        var response = all.Select(MapPlan).ToArray();
        return Result.Success<IReadOnlyList<PlanAdminResponse>>(response);
    }

    private static PlanAdminResponse MapPlan(SubscriptionPlan plan) =>
        new(
            plan.Id,
            plan.Code.Value,
            plan.Name,
            plan.Description,
            plan.Tier.ToString(),
            plan.Status.ToString(),
            plan.CreatedAtUtc,
            plan.UpdatedAtUtc,
            plan.Versions.OrderBy(version => version.VersionNumber).Select(MapVersion).ToArray()
        );

    private static PlanVersionAdminResponse MapVersion(SubscriptionPlanVersion version) =>
        new(
            version.Id,
            version.VersionNumber,
            version.Status.ToString(),
            version.EffectiveFromUtc,
            version.EffectiveUntilUtc,
            version.TrialDaysDefault,
            version.SupportedBillingCycles.Select(cycle => cycle.ToString()).ToArray(),
            version
                .Features.Select(feature => new PlanFeatureAdminResponse(
                    feature.FeatureKey.Value,
                    feature.DefaultEnabled,
                    feature.Description
                ))
                .ToArray(),
            version
                .Entitlements.Select(entitlement => new PlanEntitlementAdminResponse(
                    entitlement.Key.Value,
                    entitlement.ValueType.ToString(),
                    entitlement.DefaultValue,
                    entitlement.Description
                ))
                .ToArray(),
            version
                .PriceTiers.Select(tier => new PlanPriceTierAdminResponse(
                    tier.BillingCycle.ToString(),
                    tier.MinQuantity,
                    tier.MaxQuantity,
                    tier.UnitAmount.Amount,
                    tier.UnitAmount.Currency
                ))
                .ToArray()
        );
}
