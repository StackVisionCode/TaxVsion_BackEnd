using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Plan, add-on y suscripción mínimos para probar las dos compras de add-on (off-session y checkout).</summary>
public static class AddOnTestCatalog
{
    public static SubscriptionPlan PlanWith(string code, string[] modules)
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        version.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(
                    version.Id,
                    EntitlementKey.Create("seats.max").Value,
                    EntitlementValueType.Int,
                    "5",
                    "seats.max"
                )
                .Value
        );
        foreach (var module in modules)
        {
            version.AddFeature(
                PlanFeature.Create(version.Id, EntitlementKey.Create($"module.{module}").Value, true, module).Value
            );
        }
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(49m, "USD").Value).Value
        );
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        return plan;
    }

    public static AddOnDefinition ModuleAddOn(string code, string? module, bool allowMultiple = false)
    {
        var nowUtc = DateTime.UtcNow;
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create(code).Value,
                code,
                code,
                "module",
                allowMultiple,
                [BillingCycle.Monthly],
                Guid.Empty,
                nowUtc
            )
            .Value;
        if (module is not null)
        {
            definition.AddFeature(
                AddOnFeature.Create(definition.Id, EntitlementKey.Create($"module.{module}").Value, true).Value
            );
        }
        definition.AddPriceTier(
            AddOnPriceTier.Create(definition.Id, BillingCycle.Monthly, 1, null, Money.Create(29m, "USD").Value).Value
        );
        definition.Publish(Guid.Empty, nowUtc);
        return definition;
    }

    public static TenantSubscription ActiveSubscription(Guid tenantId, SubscriptionPlan plan)
    {
        var nowUtc = DateTime.UtcNow;
        return TenantSubscription
            .ActivateImmediately(
                tenantId,
                plan,
                plan.GetPublishedVersion()!,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddDays(30),
                Guid.Empty,
                nowUtc
            )
            .Value;
    }

    public static TenantAddOn Owned(Guid tenantId, AddOnDefinition definition) =>
        TenantAddOn
            .Purchase(
                tenantId,
                definition,
                quantity: 1,
                Money.Create(29m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
}
