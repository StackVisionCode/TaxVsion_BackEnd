using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Catalog;

namespace TaxVision.Subscription.Application.Catalog;

internal static class ModuleCatalogRules
{
    public static Result Apply(
        Module module,
        string category,
        decimal? addonPriceMonthlyUsd,
        decimal? addonPriceYearlyUsd
    )
    {
        if (!Enum.TryParse<ModuleCategory>(category, ignoreCase: true, out var parsedCategory))
            return Result.Failure(new Error("Module.InvalidCategory", "Category must be either Core or Commercial."));

        if (parsedCategory == ModuleCategory.Core)
            return module.MarkAsCore();

        var marked = module.MarkAsCommercial();
        if (marked.IsFailure)
            return marked;

        if (addonPriceMonthlyUsd is null && addonPriceYearlyUsd is null)
            return module.RemoveAddonPricing();

        if (addonPriceMonthlyUsd is null || addonPriceYearlyUsd is null)
            return Result.Failure(
                new Error(
                    "Module.IncompleteAddonPricing",
                    "Monthly and yearly add-on prices must both be provided or both omitted."
                )
            );

        return module.SetAsAddon(addonPriceMonthlyUsd.Value, addonPriceYearlyUsd.Value);
    }
}
