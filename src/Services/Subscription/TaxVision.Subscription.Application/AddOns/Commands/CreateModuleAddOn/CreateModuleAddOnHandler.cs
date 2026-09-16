using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.AddOns.Commands.CreateModuleAddOn;

public static class CreateModuleAddOnHandler
{
    public static async Task<Result<Guid>> Handle(
        CreateModuleAddOnCommand command,
        IAddOnDefinitionRepository addOnDefinitions,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var code = AddOnCode.Create(command.Code);
        if (code.IsFailure)
            return Result.Failure<Guid>(code.Error);

        if (await addOnDefinitions.GetByCodeAsync(command.Code, ct) is not null)
            return Result.Failure<Guid>(new Error("AddOn.CodeExists", "An add-on with this code already exists."));

        var moduleKey = EntitlementKey.Create($"module.{command.Module}");
        if (moduleKey.IsFailure)
            return Result.Failure<Guid>(moduleKey.Error);

        var built = Build(command, code.Value, moduleKey.Value);
        if (built.IsFailure)
            return Result.Failure<Guid>(built.Error);

        await addOnDefinitions.AddAsync(built.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(built.Value.Id);
    }

    private static Result<AddOnDefinition> Build(
        CreateModuleAddOnCommand command,
        AddOnCode code,
        EntitlementKey moduleKey
    )
    {
        var nowUtc = DateTime.UtcNow;
        var definition = AddOnDefinition.Create(
            code,
            command.Name,
            $"Modulo {command.Name} a la carta.",
            category: "module",
            allowMultipleInstances: false,
            supportedBillingCycles: [BillingCycle.Monthly, BillingCycle.Yearly],
            command.ActorUserId,
            nowUtc
        );
        if (definition.IsFailure)
            return definition;

        var addOn = definition.Value;

        var feature = AddOnFeature.Create(addOn.Id, moduleKey, enabled: true);
        if (feature.IsFailure)
            return Result.Failure<AddOnDefinition>(feature.Error);
        addOn.AddFeature(feature.Value);

        var tiers = BuildTiers(addOn.Id, command.MonthlyUsd, command.YearlyUsd);
        if (tiers.IsFailure)
            return Result.Failure<AddOnDefinition>(tiers.Error);
        foreach (var tier in tiers.Value)
            addOn.AddPriceTier(tier);

        addOn.Publish(command.ActorUserId, nowUtc);
        return Result.Success(addOn);
    }

    // Compartido con SetAddOnPrices: un tramo mensual y uno anual desde cantidad 1.
    internal static Result<IReadOnlyList<AddOnPriceTier>> BuildTiers(
        Guid addOnId,
        decimal monthlyUsd,
        decimal yearlyUsd
    )
    {
        var monthly = Money.Create(monthlyUsd, "USD");
        var yearly = Money.Create(yearlyUsd, "USD");
        if (monthly.IsFailure)
            return Result.Failure<IReadOnlyList<AddOnPriceTier>>(monthly.Error);
        if (yearly.IsFailure)
            return Result.Failure<IReadOnlyList<AddOnPriceTier>>(yearly.Error);

        var monthlyTier = AddOnPriceTier.Create(
            addOnId,
            BillingCycle.Monthly,
            minQuantity: 1,
            maxQuantity: null,
            monthly.Value
        );
        var yearlyTier = AddOnPriceTier.Create(
            addOnId,
            BillingCycle.Yearly,
            minQuantity: 1,
            maxQuantity: null,
            yearly.Value
        );
        if (monthlyTier.IsFailure)
            return Result.Failure<IReadOnlyList<AddOnPriceTier>>(monthlyTier.Error);
        if (yearlyTier.IsFailure)
            return Result.Failure<IReadOnlyList<AddOnPriceTier>>(yearlyTier.Error);

        return Result.Success<IReadOnlyList<AddOnPriceTier>>([monthlyTier.Value, yearlyTier.Value]);
    }
}
