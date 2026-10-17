using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Plans;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Plans.Commands.ReplaceDraftVersion;

/// <summary>CAT-F2.4 — orquesta el reemplazo total del contenido de una versión Draft:
/// construye los VOs (EntitlementKey, Money) y hijos (PlanFeature, PlanEntitlementDefinition,
/// PlanPriceTier) en memoria, los valida, y los pasa a <see cref="SubscriptionPlanVersion.ReplaceDraftContent"/>.
/// Módulos entran como PlanFeature con key <c>module.X</c> (mismo contrato que ReviseModules).</summary>
public static class ReplaceDraftVersionHandler
{
    public static async Task<Result> Handle(
        ReplaceDraftVersionCommand command,
        IPlanRepository plans,
        IModuleRepository modules,
        IUnitOfWork unitOfWork,
        ILogger<SubscriptionPlan> logger,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var version = plan.Versions.FirstOrDefault(v => v.Id == command.VersionId);
        if (version is null)
            return Result.Failure(new Error("PlanVersion.NotFound", "Version does not exist on this plan."));

        // Guard por 409: aggregate rechaza con PlanVersion.NotDraft si no es Draft; la map a HTTP ocurre
        // en Error.ToHttpStatusCode. Lo anticipamos acá para log explícito.
        if (version.Status != PlanVersionStatus.Draft)
            return Result.Failure(
                new Error("PlanVersion.NotDraft", "Only Draft versions can be replaced. Create a new version first.")
            );

        var cycles = new List<BillingCycle>(command.SupportedBillingCycles.Count);
        foreach (var raw in command.SupportedBillingCycles)
        {
            if (!Enum.TryParse<BillingCycle>(raw, ignoreCase: true, out var cycle))
                return Result.Failure(new Error("Plan.InvalidBillingCycle", $"Unknown billing cycle '{raw}'."));
            cycles.Add(cycle);
        }

        var selection = await PlanModuleSelection.ComposeAsync(command.Modules, modules, ct);
        if (selection.IsFailure)
            return Result.Failure(selection.Error);

        var completeModules = selection.Value;

        var features = new List<PlanFeature>();

        foreach (var module in completeModules)
        {
            var keyResult = EntitlementKey.Create($"module.{module}");
            if (keyResult.IsFailure)
                return Result.Failure(keyResult.Error);
            var featureResult = PlanFeature.Create(
                command.VersionId,
                keyResult.Value,
                defaultEnabled: true,
                description: $"module.{module}"
            );
            if (featureResult.IsFailure)
                return Result.Failure(featureResult.Error);
            features.Add(featureResult.Value);
        }

        foreach (var featureInput in command.Features)
        {
            if (featureInput.Key.StartsWith("module.", StringComparison.Ordinal))
                return Result.Failure(
                    new Error(
                        "Plan.ModuleAsFeature",
                        "Features with 'module.*' prefix must come from Modules, not Features."
                    )
                );

            var keyResult = EntitlementKey.Create(featureInput.Key);
            if (keyResult.IsFailure)
                return Result.Failure(keyResult.Error);
            var featureResult = PlanFeature.Create(
                command.VersionId,
                keyResult.Value,
                featureInput.DefaultEnabled,
                featureInput.Description
            );
            if (featureResult.IsFailure)
                return Result.Failure(featureResult.Error);
            features.Add(featureResult.Value);
        }

        var entitlements = new List<PlanEntitlementDefinition>();
        foreach (var entInput in command.Entitlements)
        {
            if (!Enum.TryParse<EntitlementValueType>(entInput.ValueType, ignoreCase: true, out var valueType))
                return Result.Failure(
                    new Error("Entitlement.InvalidValueType", $"Unknown entitlement value type '{entInput.ValueType}'.")
                );

            var keyResult = EntitlementKey.Create(entInput.Key);
            if (keyResult.IsFailure)
                return Result.Failure(keyResult.Error);

            var entResult = PlanEntitlementDefinition.Create(
                command.VersionId,
                keyResult.Value,
                valueType,
                entInput.DefaultValue,
                entInput.Description
            );
            if (entResult.IsFailure)
                return Result.Failure(entResult.Error);
            entitlements.Add(entResult.Value);
        }

        var priceTiers = new List<PlanPriceTier>();
        foreach (var tierInput in command.PriceTiers)
        {
            if (!Enum.TryParse<BillingCycle>(tierInput.BillingCycle, ignoreCase: true, out var cycle))
                return Result.Failure(
                    new Error("Plan.InvalidBillingCycle", $"Unknown billing cycle '{tierInput.BillingCycle}'.")
                );

            var amountResult = Money.Create(tierInput.Amount, tierInput.Currency);
            if (amountResult.IsFailure)
                return Result.Failure(amountResult.Error);

            var tierResult = PlanPriceTier.Create(
                command.VersionId,
                cycle,
                tierInput.MinQuantity,
                tierInput.MaxQuantity,
                amountResult.Value
            );
            if (tierResult.IsFailure)
                return Result.Failure(tierResult.Error);
            priceTiers.Add(tierResult.Value);
        }

        var replaced = version.ReplaceDraftContent(
            features,
            entitlements,
            priceTiers,
            command.TrialDaysDefault,
            cycles
        );
        if (replaced.IsFailure)
            return replaced;

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Plan {PlanId} draft version {VersionNumber} replaced by {ActorUserId}: {ModuleCount} modules, {FeatureCount} features, {EntitlementCount} entitlements, {PriceCount} price tiers.",
            command.PlanId,
            version.VersionNumber,
            command.ActorUserId,
            completeModules.Count,
            command.Features.Count,
            command.Entitlements.Count,
            command.PriceTiers.Count
        );

        return Result.Success();
    }
}
