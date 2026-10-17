using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Catalog.Queries;

public static class GetModulesForAdminHandler
{
    public static async Task<Result<IReadOnlyList<ModuleAdminResponse>>> Handle(
        GetModulesForAdminQuery query,
        IModuleRepository modules,
        CancellationToken ct
    )
    {
        var response = (await modules.GetAllAsync(ct))
            .Select(module => new ModuleAdminResponse(
                module.Code,
                module.Name,
                module.Description,
                module.Category.ToString(),
                module.AddonPriceMonthlyUsd,
                module.AddonPriceYearlyUsd,
                module.IsSellableAsAddon
            ))
            .ToArray();

        return Result.Success<IReadOnlyList<ModuleAdminResponse>>(response);
    }
}
