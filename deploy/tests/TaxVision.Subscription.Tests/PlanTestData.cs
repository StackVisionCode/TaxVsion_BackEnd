using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests;

internal static class PlanTestData
{
    public static void EnsurePublishableFeature(SubscriptionPlanVersion version)
    {
        if (version.Features.Count > 0)
            return;

        version.AddFeature(
            PlanFeature
                .Create(version.Id, EntitlementKey.Create("module.customers").Value, true, "module.customers")
                .Value
        );
    }

    public static void EnsurePublishablePrice(SubscriptionPlanVersion version)
    {
        if (version.PriceTiers.Count > 0)
            return;

        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(1m, "USD").Value).Value
        );
    }
}
