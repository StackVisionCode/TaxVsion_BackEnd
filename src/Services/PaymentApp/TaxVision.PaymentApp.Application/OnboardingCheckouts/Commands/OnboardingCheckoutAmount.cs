using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;

/// <summary>Monto del checkout del alta: el bruto lo fija Subscription; el neto de Auth se valida contra él.</summary>
internal static class OnboardingCheckoutAmount
{
    /// <summary>El precio no viene del caller: lo resuelve Subscription, dueño del plan.</summary>
    public static async Task<Result<(Money Charge, long GrossCents)>> ResolveAsync(
        CreateOnboardingCheckoutCommand command,
        ISubscriptionPlanPricingClient planPricing,
        CancellationToken ct
    )
    {
        var priceResult = await planPricing.GetPriceAsync(command.PlanId, command.BillingCycle, ct);
        if (priceResult.IsFailure)
            return Result.Failure<(Money, long)>(priceResult.Error);

        // Gift/Referral: se cobra el NETO si Auth lo pasó (descuento parcial), validado contra el bruto
        // autoritativo de Subscription; si no, el bruto. El carril $0 no llega acá (Auth no invoca checkout).
        var price = priceResult.Value;
        var chargeCents = price.AmountCents;
        if (command.NetAmountCents is { } net)
        {
            if (net <= 0 || net > price.AmountCents)
                return Result.Failure<(Money, long)>(
                    new Error(
                        "Onboarding.Checkout.InvalidNet",
                        "The net amount must be greater than zero and not exceed the resolved plan price."
                    )
                );
            chargeCents = net;
        }

        var charge = Money.Create(chargeCents, price.Currency);
        return charge.IsFailure
            ? Result.Failure<(Money, long)>(charge.Error)
            : Result.Success((charge.Value, price.AmountCents));
    }
}
