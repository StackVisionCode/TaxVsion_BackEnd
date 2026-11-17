using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.AddOns;

/// <summary>Precio de la compra de un add-on hasta el fin del período: lo usan el checkout y su vista previa.</summary>
public sealed record AddOnCheckoutPrice(Money UnitPrice, BillingCycle BillingCycle, DiscountedCharge Charge);

public static class AddOnCheckoutPricing
{
    public static Result<AddOnCheckoutPrice> Price(
        TenantSubscription subscription,
        AddOnDefinition definition,
        int quantity,
        DateTime nowUtc
    )
    {
        // Co-terminación: el add-on hereda el ciclo del plan y su precio se resuelve para ese ciclo.
        var billingCycle = subscription.BillingCycle;
        var unitPrice = definition.ResolveUnitPrice(billingCycle, quantity);
        if (unitPrice.IsFailure)
            return Result.Failure<AddOnCheckoutPrice>(unitPrice.Error);

        var prorated = ProrationCalculator.InitialPeriod(
            unitPrice.Value,
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc,
            nowUtc
        );
        if (prorated.IsFailure)
            return Result.Failure<AddOnCheckoutPrice>(prorated.Error);

        var totalCents = (long)Math.Round(prorated.Value.Amount * 100m, MidpointRounding.AwayFromZero);

        // Si el descuento vigente cubre add-ons, el prorrateo sale con descuento.
        var charge = DiscountedCharge.For(
            subscription,
            DiscountChargeKind.AddOns,
            totalCents,
            unitPrice.Value.Currency,
            nowUtc
        );
        return Result.Success(new AddOnCheckoutPrice(unitPrice.Value, billingCycle, charge));
    }
}
