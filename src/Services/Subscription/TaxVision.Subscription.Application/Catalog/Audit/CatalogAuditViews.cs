using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Catalog.Audit;

/// <summary>
/// Fotos chicas y estables de cada pieza del catálogo para el "antes" y "después" de la auditoría. Se toman antes de
/// mutar (el "antes") y después (el "después"); no exponen identificadores internos de filas hijas.
/// </summary>
public static class CatalogAuditViews
{
    public static CatalogAuditTarget Target(SubscriptionPlan plan) => new("Plan", plan.Id, plan.Code.Value);

    public static CatalogAuditTarget Target(AddOnDefinition addOn) => new("AddOn", addOn.Id, addOn.Code.Value);

    public static CatalogAuditTarget Target(Module module) => new("Module", module.Id, module.Code);

    public static PlanView Of(SubscriptionPlan plan)
    {
        var published = plan.GetPublishedVersion();
        return new PlanView(
            plan.Code.Value,
            plan.Name,
            plan.Description,
            plan.Status.ToString(),
            plan.IsOnSale,
            plan.SalesRank.Value,
            plan.IsFeatured,
            published?.VersionNumber,
            plan.Versions.Count(version => version.Status == PlanVersionStatus.Draft),
            published is null ? null : PlanVersionEntitlements.GetPricesUsdByCycle(published),
            published is null ? null : CommercialTerms.Of(published).SeatsMax,
            published is null ? null : CommercialTerms.Of(published).StorageMaxBytes,
            published is null ? null : PlanVersionEntitlements.GetEnabledModules(published).Order().ToArray()
        );
    }

    /// <summary>Contenido de una edición en borrador (cambia sin pasar por la publicada).</summary>
    public static DraftView Draft(SubscriptionPlanVersion version) =>
        new(
            version.VersionNumber,
            version.Status.ToString(),
            version.TrialDaysDefault,
            version.SupportedBillingCycles.Select(cycle => cycle.ToString()).Order().ToArray(),
            PlanVersionEntitlements.GetPricesUsdByCycle(version),
            version
                .Entitlements.OrderBy(e => e.Key.Value, StringComparer.Ordinal)
                .ToDictionary(e => e.Key.Value, e => e.DefaultValue),
            version.Features.Where(f => f.DefaultEnabled).Select(f => f.FeatureKey.Value).Order().ToArray()
        );

    public static AddOnView Of(AddOnDefinition addOn) =>
        new(
            addOn.Code.Value,
            addOn.Name,
            addOn.Status.ToString(),
            addOn.AllowMultipleInstances,
            addOn
                .PriceTiers.OrderBy(tier => tier.BillingCycle)
                .ThenBy(tier => tier.MinQuantity)
                .Select(tier => new PriceView(
                    tier.BillingCycle.ToString(),
                    tier.MinQuantity,
                    tier.MaxQuantity,
                    tier.UnitAmount.Amount
                ))
                .ToArray()
        );

    public static ModuleView Of(Module module) =>
        new(
            module.Code,
            module.Name,
            module.Category.ToString(),
            module.AddonPriceMonthlyUsd,
            module.AddonPriceYearlyUsd
        );

    public static RateLimitView Of(PlanRateLimit rateLimit) =>
        new(
            rateLimit.PlanCode.Value,
            rateLimit.Category.ToString(),
            rateLimit.MultiplierOverride,
            rateLimit.HardOverridePerMinute
        );

    public static SeatPricesView Of(SeatPricing pricing) =>
        new(
            pricing
                .PriceTiers.OrderBy(tier => tier.SeatType)
                .ThenBy(tier => tier.BillingCycle)
                .Select(tier => new SeatPriceView(
                    tier.SeatType.ToString(),
                    tier.BillingCycle.ToString(),
                    tier.UnitAmount.Amount
                ))
                .ToArray()
        );

    public sealed record PlanView(
        string Code,
        string Name,
        string Description,
        string Status,
        bool IsOnSale,
        int Rank,
        bool IsFeatured,
        int? PublishedEdition,
        int DraftEditions,
        IReadOnlyDictionary<string, decimal>? PricesUsd,
        int? SeatsMax,
        long? StorageMaxBytes,
        string[]? Modules
    );

    public sealed record DraftView(
        int Edition,
        string Status,
        int TrialDays,
        string[] Cycles,
        IReadOnlyDictionary<string, decimal> PricesUsd,
        IReadOnlyDictionary<string, string> Entitlements,
        string[] Features
    );

    public sealed record PriceView(string Cycle, int MinQuantity, int? MaxQuantity, decimal Amount);

    public sealed record AddOnView(string Code, string Name, string Status, bool AllowMultiple, PriceView[] Prices);

    public sealed record ModuleView(
        string Code,
        string Name,
        string Category,
        decimal? AddOnMonthlyUsd,
        decimal? AddOnYearlyUsd
    );

    public sealed record RateLimitView(string PlanCode, string Category, decimal Multiplier, int? HardPerMinute);

    public sealed record SeatPriceView(string SeatType, string Cycle, decimal Amount);

    public sealed record SeatPricesView(SeatPriceView[] Prices);
}
