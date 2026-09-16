using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.Commands.CreateModuleAddOn;

namespace TaxVision.Subscription.Application.AddOns.Commands.SetAddOnPrices;

public static class SetAddOnPricesHandler
{
    public static async Task<Result> Handle(
        SetAddOnPricesCommand command,
        IAddOnDefinitionRepository addOnDefinitions,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var definition = await addOnDefinitions.GetByIdForUpdateAsync(command.AddOnDefinitionId, ct);
        if (definition is null)
            return Result.Failure(new Error("AddOn.NotFound", "Add-on does not exist."));

        var tiers = CreateModuleAddOnHandler.BuildTiers(definition.Id, command.MonthlyUsd, command.YearlyUsd);
        if (tiers.IsFailure)
            return Result.Failure(tiers.Error);

        var replaced = definition.ReplacePriceTiers(tiers.Value, command.ActorUserId, DateTime.UtcNow);
        if (replaced.IsFailure)
            return replaced;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
