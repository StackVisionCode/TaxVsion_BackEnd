using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// Lee valores de entitlements/features de una <see cref="SubscriptionPlanVersion"/> para
/// traducirlos a payloads de eventos de integración legacy (MaxUsers, EnabledModules, ...)
/// que Auth y CloudStorage ya consumen. Vive en Application (no en Domain) porque es
/// lógica de traducción hacia consumidores externos, no una invariante del aggregate.
/// </summary>
public static class PlanVersionEntitlements
{
    private const string ModuleFeaturePrefix = "module.";

    public static int GetInt(SubscriptionPlanVersion version, string key, int fallback)
    {
        var raw = FindEntitlementValue(version, key);
        return raw is not null && int.TryParse(raw, out var value) ? value : fallback;
    }

    public static long GetLong(SubscriptionPlanVersion version, string key, long fallback)
    {
        var raw = FindEntitlementValue(version, key);
        return raw is not null && long.TryParse(raw, out var value) ? value : fallback;
    }

    public static decimal GetMonthlyPriceUsd(SubscriptionPlanVersion version) =>
        version.PriceTiers.FirstOrDefault(tier => tier.BillingCycle == BillingCycle.Monthly)?.UnitAmount.Amount ?? 0m;

    public static string[] GetEnabledModules(SubscriptionPlanVersion version) =>
        version.Features
            .Where(feature => feature.DefaultEnabled && feature.FeatureKey.Value.StartsWith(ModuleFeaturePrefix, StringComparison.Ordinal))
            .Select(feature => feature.FeatureKey.Value[ModuleFeaturePrefix.Length..])
            .ToArray();

    private static string? FindEntitlementValue(SubscriptionPlanVersion version, string key) =>
        version.Entitlements.FirstOrDefault(entitlement => entitlement.Key.Value == key)?.DefaultValue;
}
