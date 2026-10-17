namespace TaxVision.Subscription.Application.Catalog.Queries;

public sealed record ModuleAdminResponse(
    string Code,
    string Name,
    string Description,
    string Category,
    decimal? AddonPriceMonthlyUsd,
    decimal? AddonPriceYearlyUsd,
    bool IsSellableAsAddon
);
