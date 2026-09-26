using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions;

public enum PlanChangeDirection
{
    /// <summary>Ya está en ese plan y ciclo: no hay nada que cambiar.</summary>
    None,

    /// <summary>Destino más caro: se cobra el precio completo antes de aplicarlo.</summary>
    Upgrade,

    /// <summary>Destino igual o más barato: se agenda para el fin del período, sin cobrar.</summary>
    Downgrade,
}

/// <summary>Qué plan, a qué precio y en qué dirección. Lo mismo que se muestra y lo que se aplica.</summary>
public sealed record PlanChangeResolution(
    PlanChangeDirection Direction,
    SubscriptionPlan Plan,
    SubscriptionPlanVersion PlanVersion,
    BillingCycle? RequestedCycle,
    BillingCycle EffectiveCycle,
    long TargetAmountCents,
    string Currency,
    long CurrentAmountCents
);

/// <summary>
/// Resuelve un cambio de plan pedido. Lo comparten la vista previa y la ejecución, para que la pantalla no
/// pueda prometer algo distinto de lo que el backend va a hacer. La dirección se decide comparando el precio
/// COMPLETO del destino contra el COMPLETO del actual, igual que siempre: acá no se prorratea nada.
/// </summary>
public static class PlanChangeResolver
{
    public static async Task<Result<PlanChangeResolution>> ResolveAsync(
        TenantSubscription subscription,
        string? planCode,
        string? billingCycle,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByCodeAsync(planCode?.Trim().ToLowerInvariant() ?? string.Empty, ct);
        if (plan is null || plan.Status != PlanStatus.Published)
            return Fail("Plan.NotFound", "Plan does not exist.");

        var planVersion = plan.GetPublishedVersion();
        if (planVersion is null)
            return Fail("Plan.NoPublishedVersion", "Plan has no published version.");

        if (!PlanPricing.TryParseBillingCycle(billingCycle, out var requestedCycle))
            return Fail("Subscription.InvalidBillingCycle", $"'{billingCycle}' is not a valid billing cycle.");

        var effectiveCycle = requestedCycle ?? subscription.BillingCycle;
        var targetPrice = PlanPricing.ResolveBaseSubscriptionPrice(planVersion, effectiveCycle);
        if (targetPrice is null)
            return Fail("Plan.NoPriceTier", $"Plan {plan.Code.Value} has no price for billing cycle {effectiveCycle}.");

        var currentPlan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var currentPlanVersion = PlanPricing.FindVersion(currentPlan, subscription.PlanVersionId);
        var currentPrice = currentPlanVersion is null
            ? null
            : PlanPricing.ResolveBaseSubscriptionPrice(currentPlanVersion, subscription.BillingCycle);
        if (currentPrice is null)
            return Fail(
                "Plan.NoCurrentPriceTier",
                "Current plan has no resolvable price for its billing cycle; cannot determine upgrade/downgrade direction."
            );

        var cycleChanged = requestedCycle is not null && requestedCycle.Value != subscription.BillingCycle;
        var samePlan = subscription.PlanId == plan.Id && subscription.PlanVersionId == planVersion.Id && !cycleChanged;

        var direction =
            samePlan ? PlanChangeDirection.None
            : targetPrice.Value.AmountCents > currentPrice.Value.AmountCents ? PlanChangeDirection.Upgrade
            : PlanChangeDirection.Downgrade;

        return Result.Success(
            new PlanChangeResolution(
                direction,
                plan,
                planVersion,
                requestedCycle,
                effectiveCycle,
                targetPrice.Value.AmountCents,
                targetPrice.Value.Currency,
                currentPrice.Value.AmountCents
            )
        );
    }

    private static Result<PlanChangeResolution> Fail(string code, string message) =>
        Result.Failure<PlanChangeResolution>(new Error(code, message));
}
