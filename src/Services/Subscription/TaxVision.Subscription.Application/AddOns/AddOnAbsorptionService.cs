using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Plans;
using Wolverine;

namespace TaxVision.Subscription.Application.AddOns;

/// <summary>
/// Absorción de add-ons en un cambio de plan: cancela los add-ons de tipo módulo que el plan destino
/// ya cubre, para no cobrar dos veces la misma feature. No hace SaveChanges — lo hace el llamador,
/// junto con el recálculo de entitlements que ya dispara tras el cambio de plan.
/// </summary>
public static class AddOnAbsorptionService
{
    private const string AbsorbedReason = "Absorbed by plan upgrade";

    public static async Task AbsorbCoveredByPlanAsync(
        Guid tenantId,
        SubscriptionPlanVersion planVersion,
        ITenantAddOnRepository tenantAddOns,
        IAddOnDefinitionRepository addOnDefinitions,
        IMessageBus bus,
        ISubscriptionMetrics metrics,
        string correlationId,
        Guid actorUserId,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken ct
    )
    {
        var planModules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in planVersion.Features)
        {
            if (feature.DefaultEnabled && feature.FeatureKey.Value.StartsWith("module.", StringComparison.Ordinal))
                planModules.Add(feature.FeatureKey.Value);
        }
        if (planModules.Count == 0)
            return;

        var addOns = await tenantAddOns.GetByTenantIdForUpdateAsync(tenantId, ct);
        var absorbedCount = 0;
        var absorbedAmount = 0m;
        string? currency = null;

        foreach (var addOn in addOns)
        {
            var definition = await addOnDefinitions.GetByIdAsync(addOn.AddOnDefinitionId, ct);
            if (definition is null || !definition.IsAbsorbedByPlanModules(planModules))
                continue;

            var superseded = addOn.SupersedeByPlan(AbsorbedReason, actorUserId, nowUtc);
            if (superseded.IsFailure)
                continue;

            await bus.PublishAsync(
                new AddOnCancelledIntegrationEvent
                {
                    TenantId = tenantId,
                    CorrelationId = correlationId,
                    TenantAddOnId = addOn.Id,
                    AddOnCode = addOn.AddOnCode,
                    Reason = AbsorbedReason,
                }
            );
            metrics.RecordAddOnAbsorbed(addOn.AddOnCode);
            absorbedCount++;
            absorbedAmount += addOn.UnitPrice.Amount * addOn.Quantity;
            currency ??= addOn.UnitPrice.Currency;
        }

        if (absorbedCount > 0)
            logger.LogInformation(
                "Absorbed {Count} add-on(s) for tenant {TenantId} on plan change; saving {Amount} {Currency} per period.",
                absorbedCount,
                tenantId,
                absorbedAmount,
                currency
            );
    }
}
