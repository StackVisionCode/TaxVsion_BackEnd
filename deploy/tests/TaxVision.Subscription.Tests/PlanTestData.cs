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

    /// <summary>
    /// Publica como antes de F7: quita las claves de plataforma que publicar completa solo. Simula las
    /// ediciones viejas de la base, que el reconciliador todavía tiene que completar.
    /// </summary>
    public static void PublishAsBeforeF7(SubscriptionPlan plan, SubscriptionPlanVersion version)
    {
        var before = version.Entitlements.Select(e => e.Key.Value).ToHashSet(StringComparer.Ordinal);
        var nowUtc = DateTime.UtcNow;
        Assert.True(plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc).IsSuccess);
        var entitlements =
            (List<PlanEntitlementDefinition>)
                typeof(SubscriptionPlanVersion)
                    .GetField(
                        "_entitlements",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                    )!
                    .GetValue(version)!;
        entitlements.RemoveAll(e => !before.Contains(e.Key.Value));
    }

    public static void EnsurePublishablePrice(SubscriptionPlanVersion version)
    {
        if (version.PriceTiers.Count > 0)
            return;

        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(1m, "USD").Value).Value
        );
    }

    /// <summary>Publica una edición N+1 con otro precio mensual (y otros asientos si se pasan): lo que hace el admin.</summary>
    public static SubscriptionPlanVersion PublishNewEdition(
        SubscriptionPlan plan,
        decimal monthlyUsd,
        int? seatsMax = null
    )
    {
        var nowUtc = DateTime.UtcNow;
        var next = plan.Versions.Max(version => version.VersionNumber) + 1;
        var version = SubscriptionPlanVersion.Create(plan.Id, next, 14, [BillingCycle.Monthly]).Value;
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(monthlyUsd, "USD").Value).Value
        );
        if (seatsMax is { } seats)
            version.AddEntitlementDefinition(
                PlanEntitlementDefinition
                    .Create(
                        version.Id,
                        EntitlementKey.Create("seats.max").Value,
                        EntitlementValueType.Int,
                        seats.ToString(),
                        "seats.max"
                    )
                    .Value
            );
        EnsurePublishableFeature(version);
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        return version;
    }

    /// <summary>Plan publicado mínimo (sin eventos pendientes), para pruebas que solo necesitan que exista.</summary>
    public static SubscriptionPlan PublishedPlan(string code)
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Pro, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        EnsurePublishableFeature(version);
        EnsurePublishablePrice(version);
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        plan.ClearDomainEvents();
        return plan;
    }
}
