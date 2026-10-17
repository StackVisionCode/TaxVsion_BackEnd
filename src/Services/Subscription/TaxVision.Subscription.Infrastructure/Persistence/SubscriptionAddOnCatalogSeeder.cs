using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence;

/// <summary>
/// Siembra add-ons de cuota. Los módulos comerciales se materializan como shadows desde Modules.
/// </summary>
public static class SubscriptionAddOnCatalogSeeder
{
    public static async Task SeedAsync(SubscriptionDbContext db, CancellationToken ct)
    {
        if (await db.AddOnDefinitions.AnyAsync(ct))
            return;

        var nowUtc = DateTime.UtcNow;
        foreach (var addOn in QuotaAddOnCatalog.All)
            db.AddOnDefinitions.Add(BuildQuotaDefinition(addOn, nowUtc));

        await db.SaveChangesAsync(ct);
    }

    public static AddOnDefinition BuildModuleShadow(Module module, DateTime nowUtc)
    {
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create(module.AddOnCode).Value,
                module.Name,
                module.Description,
                category: "module",
                allowMultipleInstances: false,
                supportedBillingCycles: [BillingCycle.Monthly, BillingCycle.Yearly],
                actorUserId: Guid.Empty,
                nowUtc
            )
            .Value;

        definition.AddFeature(
            AddOnFeature
                .Create(definition.Id, EntitlementKey.Create($"module.{module.Code}").Value, enabled: true)
                .Value
        );
        definition.AddPriceTier(
            AddOnPriceTier
                .Create(
                    definition.Id,
                    BillingCycle.Monthly,
                    minQuantity: 1,
                    maxQuantity: null,
                    Money.Create(module.AddonPriceMonthlyUsd!.Value, "USD").Value
                )
                .Value
        );
        definition.AddPriceTier(
            AddOnPriceTier
                .Create(
                    definition.Id,
                    BillingCycle.Yearly,
                    minQuantity: 1,
                    maxQuantity: null,
                    Money.Create(module.AddonPriceYearlyUsd!.Value, "USD").Value
                )
                .Value
        );
        definition.Publish(Guid.Empty, nowUtc);

        return definition;
    }

    /// <summary>CAT-F3 — construye un add-on de cuota: entitlement aditivo (Sum/Max) + MutexGroup +
    /// precio. Sin AddOnFeature de módulo — <c>IsIncludedInPlan</c> lo trata como "nada que absorber".
    /// Público para que el reconciliador reutilice la misma construcción.</summary>
    public static AddOnDefinition BuildQuotaDefinition(QuotaAddOnDefinition addOn, DateTime nowUtc)
    {
        var definition = AddOnDefinition
            .Seed(
                addOn.Id,
                AddOnCode.Create(addOn.Code).Value,
                addOn.Name,
                addOn.Description,
                category: "quota",
                // Un tenant puede comprar N× del MISMO pack (storage +10 GB × 3 = +30 GB). El guard
                // de MutexGroup sigue impidiendo mezclar tiers (no small + large), y Sum ya aditiva
                // el entitlement. Patrón estándar en la industria (Google Workspace, Microsoft 365,
                // GitHub LFS legacy): un SKU por tier, N unidades por SKU, factura limpia.
                allowMultipleInstances: true,
                supportedBillingCycles: [BillingCycle.Monthly, BillingCycle.Yearly],
                nowUtc,
                mutexGroup: addOn.MutexGroup
            )
            .Value;

        definition.AddEntitlementDefinition(
            AddOnEntitlementDefinition
                .Create(
                    definition.Id,
                    EntitlementKey.Create(addOn.EntitlementKey).Value,
                    addOn.ValueType,
                    addOn.EntitlementAmount,
                    addOn.MergeStrategy
                )
                .Value
        );
        definition.AddPriceTier(
            AddOnPriceTier
                .Create(
                    definition.Id,
                    BillingCycle.Monthly,
                    minQuantity: 1,
                    maxQuantity: null,
                    Money.Create(addOn.MonthlyUsd, "USD").Value
                )
                .Value
        );
        definition.AddPriceTier(
            AddOnPriceTier
                .Create(
                    definition.Id,
                    BillingCycle.Yearly,
                    minQuantity: 1,
                    maxQuantity: null,
                    Money.Create(addOn.MonthlyUsd * 10m, "USD").Value
                )
                .Value
        );

        if (addOn.Offered)
            definition.Publish(Guid.Empty, nowUtc);

        return definition;
    }
}
