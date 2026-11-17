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
    StartAddOnCheckoutResponse? OpenCheckout = null,
    long? DiscountCents = null,
    string? DiscountSource = null,
    string? DiscountLabel = null,
    // Checkout de otro proveedor que se canceló para abrir este; el inicio cierra su sesión.
    Guid? ReplacedPaymentId = null,
    Guid? ReplacedIntentId = null
);

public static class ReserveAddOnCheckoutHandler
{
    public static async Task<Result<AddOnCheckoutReservation>> Handle(
        ReserveAddOnCheckoutCommand reservation,
        AddOnPurchaseEligibility eligibility,
        IAddOnPurchaseIntentRepository intents,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var replaced = await ReplaceOtherProviderAsync(reservation, intents, unitOfWork, ct);
        var reserved = await ReserveAsync(reservation, eligibility, intents, unitOfWork, ct);
        return replaced is null || reserved.IsFailure
            ? reserved
            : Result.Success(
                reserved.Value with
                {
                    ReplacedPaymentId = replaced.PaymentId,
                    ReplacedIntentId = replaced.IntentId,
                }
            );
    }

    // Un checkout abierto con otro proveedor se cancela (y se guarda) antes de reservar el nuevo.
    private static async Task<ReplacedCheckout?> ReplaceOtherProviderAsync(
        ReserveAddOnCheckoutCommand reservation,
        IAddOnPurchaseIntentRepository intents,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var command = reservation.Request;
        var open = await intents.GetOpenByTenantAsync(command.TenantId, command.AddOnCode.Trim(), nowUtc, ct);
        if (open is null || !open.IsOpen(nowUtc) || open.OpenedWith(command.Provider))
            return null;

        open.Cancel(command.RequestedByUserId, nowUtc, CheckoutProviderSwitch.Reason);
        await unitOfWork.SaveChangesAsync(ct);
        return open.SaaSPaymentId is { } paymentId ? new ReplacedCheckout(open.Id, paymentId) : null;
    }

    private static async Task<Result<AddOnCheckoutReservation>> ReserveAsync(
        ReserveAddOnCheckoutCommand reservation,
        AddOnPurchaseEligibility eligibility,
        IAddOnPurchaseIntentRepository intents,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var command = reservation.Request;
        var eligible = await eligibility.EnsurePurchasableAsync(
            command.TenantId,
            command.AddOnCode,
            command.Quantity,
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
            return Reserved(
                unattached,
                definition,
                AddOnCheckoutPricing.Price(subscription, definition, command.Quantity, DateTime.UtcNow)
            );

        var prepared = PrepareIntent(command, subscription, definition);
        if (prepared.IsFailure)
            return Result.Failure<AddOnCheckoutReservation>(prepared.Error);

        var (intent, charge) = prepared.Value;
        await intents.AddAsync(intent, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new AddOnCheckoutReservation(
                intent.Id,
                intent.ProratedTotal.Cents,
                intent.ProratedTotal.Currency,
                intent.Quantity,
                definition.Name,
                DiscountCents: charge.DiscountCents,
                DiscountSource: charge.Source,
                DiscountLabel: charge.Label
            )
        );
    }

    // El intent guarda el neto; el descuento se toma del precio recién calculado si ese neto coincide.
    private static Result<AddOnCheckoutReservation> Reserved(
        AddOnPurchaseIntent intent,
        AddOnDefinition definition,
        Result<AddOnCheckoutPrice> priced
    )
    {
        var charge =
            priced.IsSuccess && priced.Value.Charge.NetCents == intent.ProratedTotal.Cents ? priced.Value.Charge : null;
        return Result.Success(
            new AddOnCheckoutReservation(
                intent.Id,
                intent.ProratedTotal.Cents,
                intent.ProratedTotal.Currency,
                intent.Quantity,
                definition.Name,
                DiscountCents: charge?.DiscountCents,
                DiscountSource: charge?.Source,
                DiscountLabel: charge?.Label
            )
        );
    }

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
                open.ProratedTotal.Cents,
                open.ProratedTotal.Currency,
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

    private static Result<(AddOnPurchaseIntent Intent, DiscountedCharge Charge)> PrepareIntent(
        StartAddOnCheckoutCommand command,
        TenantSubscription subscription,
        AddOnDefinition definition
    )
    {
        var nowUtc = DateTime.UtcNow;
        var priced = AddOnCheckoutPricing.Price(subscription, definition, command.Quantity, nowUtc);
        if (priced.IsFailure)
            return Result.Failure<(AddOnPurchaseIntent, DiscountedCharge)>(priced.Error);

        var (unitPrice, billingCycle, charge) = priced.Value;
        if (charge.GrossCents <= 0)
            return Result.Failure<(AddOnPurchaseIntent, DiscountedCharge)>(
                new Error(
                    "AddOn.Checkout.NothingToCharge",
                    "This add-on is free for the current period; use the direct purchase instead of checkout."
                )
            );

        var intent = AddOnPurchaseIntent.Create(
            command.TenantId,
            definition,
            command.Quantity,
            command.AutoRenew,
            unitPrice,
            billingCycle,
            Money.FromCents(charge.NetCents, unitPrice.Currency),
            command.RequestedByUserId,
            nowUtc
        );
        return intent.IsFailure
            ? Result.Failure<(AddOnPurchaseIntent, DiscountedCharge)>(intent.Error)
            : Result.Success((intent.Value, charge));
    }
}
