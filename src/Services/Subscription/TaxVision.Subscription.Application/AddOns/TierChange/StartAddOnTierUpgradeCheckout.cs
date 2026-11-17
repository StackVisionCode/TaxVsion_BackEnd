using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Subscription.Application.AddOns.TierChange;

/// <summary>Subida de tamaño pagada por checkout (oficina sin tarjeta guardada). El email sale del token.</summary>
public sealed record StartAddOnTierUpgradeCheckoutCommand(
    Guid TenantId,
    Guid TenantAddOnId,
    string TargetCode,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string Provider,
    string Method,
    Guid RequestedByUserId
);

/// <summary>Paso transaccional: deja confirmada la intención de la subida antes de llamar a PaymentApp.</summary>
public sealed record ReserveAddOnTierUpgradeCheckoutCommand(
    Guid TenantId,
    Guid TenantAddOnId,
    string TargetCode,
    Guid RequestedByUserId,
    // Proveedor elegido: con otro distinto al del pago abierto, ese se cancela y se abre otro.
    string? Provider = null
);

public static class ReserveAddOnTierUpgradeCheckoutHandler
{
    public static async Task<Result<AddOnCheckoutReservation>> Handle(
        ReserveAddOnTierUpgradeCheckoutCommand command,
        AddOnTierChangeLoader loader,
        IAddOnPurchaseIntentRepository intents,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var nowUtc = DateTime.UtcNow;
        var loaded = await loader.LoadAsync(command.TenantId, command.TenantAddOnId, command.TargetCode, nowUtc, ct);
        if (loaded.IsFailure)
            return Result.Failure<AddOnCheckoutReservation>(loaded.Error);

        var context = loaded.Value;
        if (context.Plan.ChargeNow.Cents <= 0 && context.Plan.Direction == AddOnTierChangeDirection.Upgrade)
            return Result.Failure<AddOnCheckoutReservation>(
                new Error("AddOn.Checkout.NothingToCharge", "Nothing to charge now; change the size directly.")
            );

        // Guard del doble cobro: con una compra de ese tamaño esperando pago no se abre otra.
        var open = await intents.GetOpenByTenantAsync(command.TenantId, context.Target.Code.Value, nowUtc, ct);
        ReplacedCheckout? replaced = null;
        if (open is not null && open.IsOpen(nowUtc))
        {
            if (open.OpenedWith(command.Provider))
                return Result.Failure<AddOnCheckoutReservation>(
                    new Error(
                        "AddOn.CheckoutInProgress",
                        "There is already a purchase of this size waiting for payment."
                    )
                );

            open.Cancel(command.RequestedByUserId, nowUtc, CheckoutProviderSwitch.Reason);
            replaced = open.SaaSPaymentId is { } paymentId ? new ReplacedCheckout(open.Id, paymentId) : null;
        }

        var intent = AddOnPurchaseIntent.CreateTierUpgrade(
            context.AddOn,
            context.Target,
            context.Plan,
            command.RequestedByUserId,
            nowUtc
        );
        if (intent.IsFailure)
            return Result.Failure<AddOnCheckoutReservation>(intent.Error);

        await intents.AddAsync(intent.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(
            new AddOnCheckoutReservation(
                intent.Value.Id,
                intent.Value.ProratedTotal.Cents,
                intent.Value.ProratedTotal.Currency,
                intent.Value.Quantity,
                context.Target.Name,
                ReplacedPaymentId: replaced?.PaymentId,
                ReplacedIntentId: replaced?.IntentId
            )
        );
    }
}

/// <summary>Orquesta sin transacción, igual que <see cref="StartAddOnCheckoutHandler"/>: reserva, sesión de pago y
/// vínculo. El add-on cambia de tamaño cuando el pago se confirma.</summary>
public static class StartAddOnTierUpgradeCheckoutHandler
{
    [NonTransactional]
    public static async Task<Result<StartAddOnCheckoutResponse>> Handle(
        StartAddOnTierUpgradeCheckoutCommand command,
        IMessageBus bus,
        IAddOnCheckoutPaymentClient checkoutClient,
        CancellationToken ct,
        ICorrelationContext? correlation = null
    )
    {
        var reserved = await bus.InvokeAsync<Result<AddOnCheckoutReservation>>(
            new ReserveAddOnTierUpgradeCheckoutCommand(
                command.TenantId,
                command.TenantAddOnId,
                command.TargetCode,
                command.RequestedByUserId,
                command.Provider
            ),
            ct
        );
        if (reserved.IsFailure)
            return Result.Failure<StartAddOnCheckoutResponse>(reserved.Error);

        if (reserved.Value.ReplacedPaymentId is { } replacedPayment)
        {
            await bus.PublishAsync(new CancelHostedCheckoutCommand(command.TenantId, replacedPayment));
            await bus.PublishAsync(
                new AddOnCheckoutCancelledIntegrationEvent
                {
                    TenantId = command.TenantId,
                    AddOnPurchaseIntentId = reserved.Value.ReplacedIntentId ?? Guid.Empty,
                    SaaSPaymentId = replacedPayment,
                    ActorUserId = command.RequestedByUserId,
                    Reason = CheckoutProviderSwitch.Reason,
                    CorrelationId = correlation?.CorrelationId ?? string.Empty,
                }
            );
        }

        var checkout = await checkoutClient.CreateCheckoutAsync(ClientRequest(command, reserved.Value), ct);
        if (checkout.IsFailure)
            return Result.Failure<StartAddOnCheckoutResponse>(checkout.Error);

        var session = checkout.Value;
        var attached = await bus.InvokeAsync<Result>(
            new AttachAddOnCheckoutCommand(
                command.TenantId,
                reserved.Value.IntentId,
                session.PaymentId,
                session.CheckoutUrl,
                session.ExpiresAtUtc,
                command.Provider
            ),
            ct
        );
        return attached.IsFailure
            ? Result.Failure<StartAddOnCheckoutResponse>(attached.Error)
            : Result.Success(
                new StartAddOnCheckoutResponse(
                    reserved.Value.IntentId,
                    session.CheckoutUrl,
                    session.PaymentId,
                    session.ExpiresAtUtc
                )
            );
    }

    private static AddOnCheckoutClientRequest ClientRequest(
        StartAddOnTierUpgradeCheckoutCommand command,
        AddOnCheckoutReservation reservation
    ) =>
        new(
            command.TenantId,
            reservation.IntentId,
            reservation.TotalCents,
            reservation.Currency,
            command.PayerEmail,
            command.SuccessUrl,
            command.CancelUrl,
            IdempotencyKeyFactory.AddOnCheckout(reservation.IntentId),
            command.Provider,
            command.Method,
            reservation.Quantity,
            ProratedUnit.Of(reservation.TotalCents, reservation.Quantity),
            reservation.AddOnName
        );
}
