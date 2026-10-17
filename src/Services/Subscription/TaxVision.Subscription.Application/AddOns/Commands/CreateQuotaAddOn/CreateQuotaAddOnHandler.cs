using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.AddOns.Commands.CreateQuotaAddOn;

/// <summary>Orquesta la creación y publicación de un add-on de cuota.</summary>
public static class CreateQuotaAddOnHandler
{
    public static async Task<Result<Guid>> Handle(
        CreateQuotaAddOnCommand command,
        IAddOnDefinitionRepository addOnDefinitions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var code = AddOnCode.Create(command.Code);
        if (code.IsFailure)
            return Result.Failure<Guid>(code.Error);

        if (await addOnDefinitions.GetByCodeAsync(command.Code, ct) is not null)
            return Result.Failure<Guid>(new Error("AddOn.CodeExists", "An add-on with this code already exists."));

        var entitlementKey = EntitlementKey.Create(command.EntitlementKey);
        if (entitlementKey.IsFailure)
            return Result.Failure<Guid>(entitlementKey.Error);

        if (!Enum.TryParse<EntitlementValueType>(command.ValueType, ignoreCase: true, out var valueType))
            return Result.Failure<Guid>(
                new Error("Entitlement.InvalidValueType", $"Unknown entitlement value type '{command.ValueType}'.")
            );

        if (!Enum.TryParse<AddOnMergeStrategy>(command.MergeStrategy, ignoreCase: true, out var strategy))
            return Result.Failure<Guid>(
                new Error("AddOn.InvalidMergeStrategy", $"Unknown merge strategy '{command.MergeStrategy}'.")
            );
        // Replace/Or no tienen sentido para quota (aditivo): Replace sobreescribe el plan y Or es solo
        // para booleans de feature. Sum y Max son los únicos que suman valor comercial a lo del plan.
        if (strategy is not (AddOnMergeStrategy.Sum or AddOnMergeStrategy.Max))
            return Result.Failure<Guid>(
                new Error(
                    "AddOn.MergeStrategyNotQuota",
                    "Quota add-ons require MergeStrategy = Sum or Max (Replace/Or are for module/feature add-ons)."
                )
            );

        var built = Build(command, code.Value, entitlementKey.Value, valueType, strategy);
        if (built.IsFailure)
            return Result.Failure<Guid>(built.Error);

        await addOnDefinitions.AddAsync(built.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new CatalogChangedIntegrationEvent
            {
                ChangeKind = "addon.created",
                EntityId = built.Value.Id,
                EntityCode = built.Value.Code.Value,
                CorrelationId = string.Empty,
            }
        );
        return Result.Success(built.Value.Id);
    }

    private static Result<AddOnDefinition> Build(
        CreateQuotaAddOnCommand command,
        AddOnCode code,
        EntitlementKey entitlementKey,
        EntitlementValueType valueType,
        AddOnMergeStrategy strategy
    )
    {
        var nowUtc = DateTime.UtcNow;
        var definition = AddOnDefinition.Create(
            code,
            command.Name,
            command.Description,
            category: "quota",
            allowMultipleInstances: false,
            supportedBillingCycles: [BillingCycle.Monthly, BillingCycle.Yearly],
            command.ActorUserId,
            nowUtc,
            mutexGroup: command.MutexGroup
        );
        if (definition.IsFailure)
            return definition;

        var addOn = definition.Value;

        var entitlement = AddOnEntitlementDefinition.Create(
            addOn.Id,
            entitlementKey,
            valueType,
            command.EntitlementAmount,
            strategy
        );
        if (entitlement.IsFailure)
            return Result.Failure<AddOnDefinition>(entitlement.Error);
        addOn.AddEntitlementDefinition(entitlement.Value);

        var tiers = AddOnPriceTierFactory.Create(addOn.Id, command.MonthlyUsd, command.YearlyUsd);
        if (tiers.IsFailure)
            return Result.Failure<AddOnDefinition>(tiers.Error);
        foreach (var tier in tiers.Value)
            addOn.AddPriceTier(tier);

        addOn.Publish(command.ActorUserId, nowUtc);
        return Result.Success(addOn);
    }
}
