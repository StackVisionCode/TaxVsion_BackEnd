using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Una suscripción activa mensual con el período que pida la prueba.</summary>
public static class TestSubscriptions
{
    public static TenantSubscription Active(Guid tenantId, DateTime periodStartUtc, DateTime periodEndUtc) =>
        Active(tenantId, periodStartUtc, periodEndUtc, out _);

    public static TenantSubscription Active(
        Guid tenantId,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        out SubscriptionPlan plan
    )
    {
        var now = DateTime.UtcNow;
        plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Starter, Guid.Empty, now)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(29m, "USD").Value).Value
        );
        plan.AddVersion(version, Guid.Empty, now);
        plan.PublishVersion(version.Id, now, Guid.Empty, now);
        return TenantSubscription
            .ActivateImmediately(
                tenantId,
                plan,
                version,
                BillingCycle.Monthly,
                periodStartUtc,
                periodEndUtc,
                Guid.Empty,
                now
            )
            .Value;
    }
}
