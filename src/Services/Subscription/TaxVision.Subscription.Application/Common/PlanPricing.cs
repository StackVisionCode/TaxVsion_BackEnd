using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// Resuelve el tramo de precio (cantidad 1, la suscripción base no tiene quantity) para una
/// versión de plan y ciclo de facturación dados. Subscription es la única fuente de verdad
/// del pricing — PaymentApp solo recibe el resultado ya calculado. Compartido entre el job
/// de renovación periódica y la activación self-service para no duplicar la búsqueda de tier.
/// </summary>
public static class PlanPricing
{
    public static SubscriptionPlanVersion? FindVersion(SubscriptionPlan? plan, Guid planVersionId)
    {
        if (plan is null)
            return null;

        foreach (var candidate in plan.Versions)
        {
            if (candidate.Id == planVersionId)
                return candidate;
        }

        return null;
    }

    public static (long AmountCents, string Currency)? ResolveBaseSubscriptionPrice(
        SubscriptionPlanVersion version,
        BillingCycle billingCycle
    )
    {
        foreach (var tier in version.PriceTiers)
        {
            if (tier.BillingCycle != billingCycle || tier.MinQuantity > 1)
                continue;
            if (tier.MaxQuantity is not null && tier.MaxQuantity < 1)
                continue;

            return (
                (long)Math.Round(tier.UnitAmount.Amount * 100m, MidpointRounding.AwayFromZero),
                tier.UnitAmount.Currency
            );
        }

        return null;
    }

    /// <summary>Parsea el string del request ("Monthly"/"Yearly"/etc, case-insensitive).
    /// Null/vacío es válido — significa "no se pidió cambiar el ciclo".</summary>
    public static bool TryParseBillingCycle(string? value, out BillingCycle? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!Enum.TryParse<BillingCycle>(value, ignoreCase: true, out var parsed))
            return false;

        result = parsed;
        return true;
    }
}
