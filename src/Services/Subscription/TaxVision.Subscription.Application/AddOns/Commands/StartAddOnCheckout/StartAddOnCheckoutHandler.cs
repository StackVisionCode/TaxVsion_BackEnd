using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;

/// <summary>
/// Abre (o retoma) el checkout hosteado para comprar un add-on. Molde: <c>StartSeatCheckoutHandler</c>. El
/// <c>TenantAddOn</c> NO se crea acá: solo la intención. Lo activa el consumer cuando el pago se confirma, así
/// nadie queda con un add-on sin haber pagado.
/// </summary>
public static class StartAddOnCheckoutHandler
{
    public static async Task<Result<StartAddOnCheckoutResponse>> Handle(
        StartAddOnCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IAddOnDefinitionRepository addOnDefinitions,
        ISubscriptionTenantSettingsRepository settings,
        ITenantAddOnRepository tenantAddOns,
        IAddOnPurchaseIntentRepository intents,
        IAddOnCheckoutPaymentClient checkoutClient,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var eligible = await AddOnPurchaseEligibility.EnsurePurchasableAsync(
            command.TenantId,
            command.AddOnCode,
            command.Quantity,
            subscriptions,
            plans,
            addOnDefinitions,
            settings,
            tenantAddOns,
            ct
        );
        if (eligible.IsFailure)
            return Result.Failure<StartAddOnCheckoutResponse>(eligible.Error);

        var (subscription, definition) = eligible.Value;

        var reused = await ReuseOpenCheckoutAsync(
            command,
            definition.Code.Value,
            subscription.BillingCycle,
            intents,
            ct
        );
        if (reused is not null)
            return reused;

        var prepared = PrepareIntent(command, subscription, definition);
        if (prepared.IsFailure)
            return Result.Failure<StartAddOnCheckoutResponse>(prepared.Error);

        var intent = prepared.Value;
        await intents.AddAsync(intent, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var checkout = await checkoutClient.CreateCheckoutAsync(
            new AddOnCheckoutClientRequest(
                command.TenantId,
                intent.Id,
                intent.ProratedTotalCents,
                intent.Currency,
                command.PayerEmail,
                command.SuccessUrl,
                command.CancelUrl,
                IdempotencyKeyFactory.AddOnCheckout(intent.Id),
                command.Provider,
                command.Method,
                intent.Quantity,
                ProratedUnit.Of(intent.ProratedTotalCents, intent.Quantity)
            ),
            ct
        );
        if (checkout.IsFailure)
            return Result.Failure<StartAddOnCheckoutResponse>(checkout.Error);

        intent.AttachCheckout(
            checkout.Value.PaymentId,
            checkout.Value.CheckoutUrl,
            checkout.Value.ExpiresAtUtc,
            DateTime.UtcNow
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new StartAddOnCheckoutResponse(
                intent.Id,
                checkout.Value.CheckoutUrl,
                checkout.Value.PaymentId,
                checkout.Value.ExpiresAtUtc
            )
        );
    }

    /// <summary>
    /// Guard del doble cobro: mientras la sesión anterior de ESE add-on siga viva, la misma compra devuelve esa
    /// misma URL en vez de abrir un segundo cobro. Si lo que se pide es distinto, se rechaza.
    /// </summary>
    private static async Task<Result<StartAddOnCheckoutResponse>?> ReuseOpenCheckoutAsync(
        StartAddOnCheckoutCommand command,
        string addOnCode,
        BillingCycle billingCycle,
        IAddOnPurchaseIntentRepository intents,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var open = await intents.GetOpenByTenantAsync(command.TenantId, addOnCode, nowUtc, ct);
        if (open is null || !open.IsOpen(nowUtc))
            return null;

        if (!open.Matches(addOnCode, command.Quantity, billingCycle, command.AutoRenew))
            return Result.Failure<StartAddOnCheckoutResponse>(
                new Error(
                    "AddOn.CheckoutInProgress",
                    "There is already an add-on purchase waiting for payment. Finish it or wait for it to expire."
                )
            );

        return Result.Success(
            new StartAddOnCheckoutResponse(
                open.Id,
                open.CheckoutUrl!,
                open.SaaSPaymentId!.Value,
                open.CheckoutExpiresAtUtc!.Value
            )
        );
    }

    private static Result<AddOnPurchaseIntent> PrepareIntent(
        StartAddOnCheckoutCommand command,
        TenantSubscription subscription,
        AddOnDefinition definition
    )
    {
        // Co-terminación: el add-on hereda el ciclo del plan y su precio se resuelve para ese ciclo.
        var billingCycle = subscription.BillingCycle;
        var unitPrice = definition.ResolveUnitPrice(billingCycle, command.Quantity);
        if (unitPrice.IsFailure)
            return Result.Failure<AddOnPurchaseIntent>(unitPrice.Error);

        var prorated = ProrationCalculator.InitialPeriod(
            unitPrice.Value,
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc,
            DateTime.UtcNow
        );
        if (prorated.IsFailure)
            return Result.Failure<AddOnPurchaseIntent>(prorated.Error);

        var totalCents = (long)Math.Round(prorated.Value.Amount * 100m, MidpointRounding.AwayFromZero);
        if (totalCents <= 0)
            return Result.Failure<AddOnPurchaseIntent>(
                new Error(
                    "AddOn.Checkout.NothingToCharge",
                    "This add-on is free for the current period; use the direct purchase instead of checkout."
                )
            );

        return AddOnPurchaseIntent.Create(
            command.TenantId,
            definition,
            command.Quantity,
            command.AutoRenew,
            unitPrice.Value,
            billingCycle,
            totalCents,
            command.RequestedByUserId,
            DateTime.UtcNow
        );
    }
}
