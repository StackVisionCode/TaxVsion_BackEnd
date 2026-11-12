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
/// pueda prometer algo distinto de lo que el backend va a hacer. La dirección sale de <see cref="Direction"/>;
/// el cobro no cambia: acá no se prorratea nada.
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

        // Pasarse a otro plan exige que esté a la venta; cambiar el ciclo del propio, no.
        if (!plan.IsOnSale && plan.Id != subscription.PlanId)
            return Fail("Plan.NotOnSale", "This plan is no longer offered.");

        if (!PlanPricing.TryParseBillingCycle(billingCycle, out var requestedCycle))
            return Fail("Subscription.InvalidBillingCycle", $"'{billingCycle}' is not a valid billing cycle.");

        // D6: en su mismo plan el cliente se queda en la edición que firmó; a otro plan entra por la publicada.
        var isCurrentPlan = plan.Id == subscription.PlanId;
        var planVersion = isCurrentPlan
            ? PlanPricing.FindVersion(plan, subscription.PlanVersionId)
            : plan.GetPublishedVersion();
        if (planVersion is null)
            return isCurrentPlan
                ? Fail("Plan.ContractedVersionNotFound", "The contracted plan edition does not exist.")
                : Fail("Plan.NoPublishedVersion", "Plan has no published version.");

        var effectiveCycle = requestedCycle ?? subscription.BillingCycle;
        var targetPrice = PlanPricing.ResolveBaseSubscriptionPrice(planVersion, effectiveCycle);
        if (targetPrice is null)
            return isCurrentPlan
                ? Fail(
                    "Plan.CycleNotAvailableForContractedVersion",
                    $"Your current plan edition has no {effectiveCycle} price. Choose a plan on sale to switch cycles."
                )
                : Fail("Plan.NoPriceTier", $"Plan {plan.Code.Value} has no price for billing cycle {effectiveCycle}.");

        var currentPlan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var currentPlanVersion = PlanPricing.FindVersion(currentPlan, subscription.PlanVersionId);
        var currentPrice = currentPlanVersion is null
            ? null
            : PlanPricing.ResolveBaseSubscriptionPrice(currentPlanVersion, subscription.BillingCycle);
        if (currentPlan is null || currentPrice is null)
            return Fail(
                "Plan.NoCurrentPriceTier",
                "Current plan has no resolvable price for its billing cycle; cannot determine upgrade/downgrade direction."
            );

        var cycleChanged = requestedCycle is not null && requestedCycle.Value != subscription.BillingCycle;
        var direction = Direction(
            isCurrentPlan,
            cycleChanged,
            (plan.SalesRank.Value, targetPrice.Value.AmountCents, effectiveCycle),
            (currentPlan.SalesRank.Value, currentPrice.Value.AmountCents, subscription.BillingCycle)
        );

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

    /// <summary>
    /// Entre planes distintos decide el rango (Pro → Enterprise es upgrade aunque cueste menos); con el mismo
    /// rango, el precio mensual equivalente. En el mismo plan, solo cambia el ciclo: como siempre, por precio completo.
    /// </summary>
    private static PlanChangeDirection Direction(
        bool isCurrentPlan,
        bool cycleChanged,
        (int Rank, long Cents, BillingCycle Cycle) target,
        (int Rank, long Cents, BillingCycle Cycle) current
    )
    {
        if (isCurrentPlan)
            return !cycleChanged ? PlanChangeDirection.None
                : target.Cents > current.Cents ? PlanChangeDirection.Upgrade
                : PlanChangeDirection.Downgrade;

        if (target.Rank != current.Rank)
            return target.Rank > current.Rank ? PlanChangeDirection.Upgrade : PlanChangeDirection.Downgrade;

        return
            PlanPricing.MonthlyEquivalentCents(target.Cents, target.Cycle)
            > PlanPricing.MonthlyEquivalentCents(current.Cents, current.Cycle)
            ? PlanChangeDirection.Upgrade
            : PlanChangeDirection.Downgrade;
    }

    private static Result<PlanChangeResolution> Fail(string code, string message) =>
        Result.Failure<PlanChangeResolution>(new Error(code, message));
}
