using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Catalog;

public sealed class Module : BaseEntity
{
    private Module() { }

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public ModuleCategory Category { get; private set; }
    public decimal? AddonPriceMonthlyUsd { get; private set; }
    public decimal? AddonPriceYearlyUsd { get; private set; }

    public bool IsSellableAsAddon =>
        Category == ModuleCategory.Commercial && AddonPriceMonthlyUsd is > 0m && AddonPriceYearlyUsd is > 0m;

    public string AddOnCode => $"module-addon-{Code}";

    public static Result<Module> Create(string code, string name, string description)
    {
        var validation = Validate(code, name, description);
        if (validation.IsFailure)
            return Result.Failure<Module>(validation.Error);

        return Result.Success(
            new Module
            {
                Code = code.Trim().ToLowerInvariant(),
                Name = name.Trim(),
                Description = description.Trim(),
                Category = ModuleCategory.Core,
            }
        );
    }

    public static Result<Module> Seed(Guid id, string code, string name, string description)
    {
        var created = Create(code, name, description);
        if (created.IsFailure)
            return created;

        created.Value.Id = id;
        return created;
    }

    public Result MarkAsCommercial()
    {
        Category = ModuleCategory.Commercial;
        return Result.Success();
    }

    public Result SetAsAddon(decimal monthlyUsd, decimal yearlyUsd)
    {
        if (Category != ModuleCategory.Commercial)
        {
            return Result.Failure(new Error("Module.CoreCannotBeAddon", "Core modules are never sold as add-ons."));
        }

        if (monthlyUsd <= 0m || yearlyUsd <= 0m)
        {
            return Result.Failure(
                new Error("Module.InvalidAddonPrice", "Monthly and yearly add-on prices must be positive.")
            );
        }

        AddonPriceMonthlyUsd = monthlyUsd;
        AddonPriceYearlyUsd = yearlyUsd;
        return Result.Success();
    }

    public Result MarkAsCore()
    {
        Category = ModuleCategory.Core;
        AddonPriceMonthlyUsd = null;
        AddonPriceYearlyUsd = null;
        return Result.Success();
    }

    public Result RemoveAddonPricing()
    {
        if (Category != ModuleCategory.Commercial)
        {
            return Result.Failure(new Error("Module.CoreCannotBeAddon", "Core modules are never sold as add-ons."));
        }

        AddonPriceMonthlyUsd = null;
        AddonPriceYearlyUsd = null;
        return Result.Success();
    }

    private static Result Validate(string code, string name, string description)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 50)
            return Result.Failure(
                new Error("Module.InvalidCode", "Code is required and must be 50 characters or fewer.")
            );

        if (code.Any(character => !(char.IsLower(character) || char.IsDigit(character) || character is '-' or '_')))
            return Result.Failure(
                new Error("Module.InvalidCode", "Code must use lowercase letters, digits, '-' or '_'.")
            );

        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return Result.Failure(
                new Error("Module.InvalidName", "Name is required and must be 120 characters or fewer.")
            );

        if (string.IsNullOrWhiteSpace(description) || description.Length > 500)
            return Result.Failure(
                new Error("Module.InvalidDescription", "Description is required and must be 500 characters or fewer.")
            );

        return Result.Success();
    }
}
