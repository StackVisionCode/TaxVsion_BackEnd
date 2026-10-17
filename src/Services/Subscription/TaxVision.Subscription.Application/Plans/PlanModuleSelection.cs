using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Catalog;

namespace TaxVision.Subscription.Application.Plans;

public static class PlanModuleSelection
{
    public static async Task<Result<IReadOnlyList<string>>> ComposeAsync(
        IReadOnlyCollection<string> selectedCommercialModules,
        IModuleRepository modules,
        CancellationToken ct
    )
    {
        var catalog = await modules.GetAllAsync(ct);
        if (catalog.Count == 0)
            return Result.Failure<IReadOnlyList<string>>(new Error("Module.CatalogEmpty", "Module catalog is empty."));

        var byCode = catalog.ToDictionary(module => module.Code, StringComparer.OrdinalIgnoreCase);
        var selected = selectedCommercialModules
            .Select(code => code.Trim().ToLowerInvariant())
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var code in selected)
        {
            if (!byCode.TryGetValue(code, out var module))
                return Result.Failure<IReadOnlyList<string>>(
                    new Error("Plan.UnknownModule", $"Module '{code}' is not registered.")
                );

            if (module.Category == ModuleCategory.Core)
                return Result.Failure<IReadOnlyList<string>>(
                    new Error("Plan.CoreModuleCannotBeExplicit", "Core modules are always included; do not list them.")
                );
        }

        var complete = catalog
            .Where(module =>
                module.Category == ModuleCategory.Core
                || selected.Contains(module.Code, StringComparer.OrdinalIgnoreCase)
            )
            .Select(module => module.Code)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return Result.Success<IReadOnlyList<string>>(complete);
    }
}
