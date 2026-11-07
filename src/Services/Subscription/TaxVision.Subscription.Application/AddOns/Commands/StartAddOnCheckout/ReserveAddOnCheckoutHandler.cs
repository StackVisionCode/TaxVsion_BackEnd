using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;

/// <summary>Paso 1 de SUB-7: confirma la intención antes de llamar a PaymentApp.</summary>
public sealed record ReserveAddOnCheckoutCommand(StartAddOnCheckoutCommand Request)
{
    public Guid TenantId => Request.TenantId;
}

/// <summary><paramref name="OpenCheckout"/> viene si ya hay una sesión viva de esa compra: no se llama a PaymentApp.</summary>
public sealed record AddOnCheckoutReservation(
    Guid IntentId,
    long TotalCents,
    string Currency,
    int Quantity,
    string AddOnName,
    StartAddOnCheckoutResponse? OpenCheckout = null
);

public static class ReserveAddOnCheckoutHandler
{
    public static async Task<Result<AddOnCheckoutReservation>> Handle(
        ReserveAddOnCheckoutCommand reservation,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IModuleRepository modules,
        IAddOnDefinitionRepository addOnDefinitions,
        ISubscriptionTenantSettingsRepository settings,
        ITenantAddOnRepository tenantAddOns,
        IAddOnPurchaseIntentRepository intents,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var command = reservation.Request;
        var eligible = await AddOnPurchaseEligibility.EnsurePurchasableAsync(
            command.TenantId,
            command.AddOnCode,
            command.Quantity,
            subscriptions,
            plans,
            modules,
            addOnDefinitions,
            settings,
            tenantAddOns,
            ct
        );
        if (eligible.IsFailure)
            return Result.Failure<AddOnCheckoutReservation>(eligible.Error);

        var (subscription, definition) = eligible.Value;

        var reused = await ReuseOpenCheckoutAsync(command, definition, subscription.BillingCycle, intents, ct);
        if (reused is not null)
            return reused;

        var unattached = await intents.GetUnattachedByTenantAsync(command.TenantId, definition.Code.Value, ct);
        if (
            unattached is not null
            && unattached.Matches(definition.Code.Value, command.Quantity, subscription.BillingCycle, command.AutoRenew)
        )
            return Reserved(unattached, definition);

        var prepared = PrepareIntent(command, subscription, definition);
        if (prepared.IsFailure)
            return Result.Failure<AddOnCheckoutReservation>(prepared.Error);

        var intent = prepared.Value;
        await intents.AddAsync(intent, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Reserved(intent, definition);
    }

    private static Result<AddOnCheckoutReservation> Reserved(AddOnPurchaseIntent intent, AddOnDefinition definition) =>
        Result.Success(
            new AddOnCheckoutReservation(
                intent.Id,
                intent.ProratedTotalCents,
                intent.Currency,
                intent.Quantity,
                definition.Name
            )
        );

    /// <summary>
    /// Guard del doble cobro: mientras la sesión anterior de ESE add-on siga viva, la misma compra devuelve esa
    /// misma URL en vez de abrir un segundo cobro. Si lo que se pide es distinto, se rechaza.
    /// </summary>
    private static async Task<Result<AddOnCheckoutReservation>?> ReuseOpenCheckoutAsync(
        StartAddOnCheckoutCommand command,
        AddOnDefinition definition,
        BillingCycle billingCycle,
        IAddOnPurchaseIntentRepository intents,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var addOnCode = definition.Code.Value;
        var open = await intents.GetOpenByTenantAsync(command.TenantId, addOnCode, nowUtc, ct);
        if (open is null || !open.IsOpen(nowUtc))
            return null;

        if (!open.Matches(addOnCode, command.Quantity, billingCycle, command.AutoRenew))
            return Result.Failure<AddOnCheckoutReservation>(
                new Error(
                    "AddOn.CheckoutInProgress",
                    "There is already an add-on purchase waiting for payment. Finish it or wait for it to expire."
                )
            );

        return Result.Success(
            new AddOnCheckoutReservation(
                open.Id,
                open.ProratedTotalCents,
                open.Currency,
                open.Quantity,
                definition.Name,
                new StartAddOnCheckoutResponse(
                    open.Id,
                    open.CheckoutUrl!,
                    open.SaaSPaymentId!.Value,
                    open.CheckoutExpiresAtUtc!.Value
                )
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
