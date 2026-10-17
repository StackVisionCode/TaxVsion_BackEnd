using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns;
using TaxVision.Subscription.Domain.AddOns;
using Wolverine;

namespace TaxVision.Subscription.Application.AddOns.Commands.SetAddOnPrices;

public static class SetAddOnPricesHandler
{
    public static async Task<Result> Handle(
        SetAddOnPricesCommand command,
        IAddOnDefinitionRepository addOnDefinitions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var definition = await addOnDefinitions.GetByIdForUpdateAsync(command.AddOnDefinitionId, ct);
        if (definition is null)
            return Result.Failure(new Error("AddOn.NotFound", "Add-on does not exist."));

        if (string.Equals(definition.Category, "module", StringComparison.OrdinalIgnoreCase))
            return Result.Failure(
                new Error("Module.ManagedByCatalog", "Module add-on prices are managed by the module catalog.")
            );

        var tiers = AddOnPriceTierFactory.Create(definition.Id, command.MonthlyUsd, command.YearlyUsd);
        if (tiers.IsFailure)
            return Result.Failure(tiers.Error);

        var replaced = definition.ReplacePriceTiers(tiers.Value, command.ActorUserId, DateTime.UtcNow);
        if (replaced.IsFailure)
            return replaced;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new CatalogChangedIntegrationEvent
            {
                ChangeKind = "addon.prices",
                EntityId = definition.Id,
                EntityCode = definition.Code.Value,
                CorrelationId = string.Empty,
            }
        );
        return Result.Success();
    }
}
