using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.StartRenewalCheckout;

/// <summary>
/// Crea la intención de renovación self-service y pide a PaymentApp la sesión de checkout hosteada. Dos usos:
/// (a) recuperarse de un lapso (PastDue/Suspended/Expired) y (b) prepagar el próximo ciclo cuando la sub
/// sigue Active/Trialing. En ambos casos el monto es el precio del ciclo vigente, resuelto server-side.
/// El consumer del webhook distingue la ruta por el status actual: reactiva en lapso, avanza período en Active.
/// Molde: <c>StartSeatCheckoutHandler</c>. SUB-7: orquesta sin transacción, igual que el molde.
/// </summary>
public static class StartRenewalCheckoutHandler
{
    [NonTransactional]
    public static async Task<Result<StartRenewalCheckoutResponse>> Handle(
        StartRenewalCheckoutCommand command,
        IMessageBus bus,
        IRenewalCheckoutPaymentClient checkoutClient,
        CancellationToken ct,
        ICorrelationContext? correlation = null
    )
    {
        var reserved = await bus.InvokeAsync<Result<RenewalCheckoutReservation>>(
            new ReserveRenewalCheckoutCommand(command),
            ct
        );
        if (reserved.IsFailure)
            return Result.Failure<StartRenewalCheckoutResponse>(reserved.Error);

        var reservation = reserved.Value;
        // Volvió con otro proveedor: la sesión del checkout reemplazado deja de ser pagable.
        if (reservation.ReplacedPaymentId is { } replacedPayment)
        {
            await bus.PublishAsync(new CancelHostedCheckoutCommand(command.TenantId, replacedPayment));
            // Mismo aviso que al cancelar a mano: PaymentApp marca el pago como cancelado.
            await bus.PublishAsync(
                new SubscriptionRenewalCheckoutCancelledIntegrationEvent
                {
                    TenantId = command.TenantId,
                    RenewalIntentId = reservation.ReplacedIntentId ?? Guid.Empty,
                    SaaSPaymentId = replacedPayment,
                    ActorUserId = command.RequestedByUserId,
                    Reason = CheckoutProviderSwitch.Reason,
                    CorrelationId = correlation?.CorrelationId ?? string.Empty,
                }
            );
        }
        if (reservation.OpenCheckout is { } open)
            return Result.Success(open);

        var checkout = await checkoutClient.CreateCheckoutAsync(
            new RenewalCheckoutClientRequest(
                command.TenantId,
                reservation.IntentId,
                reservation.AmountCents,
                reservation.Currency,
                command.PayerEmail,
                command.SuccessUrl,
                command.CancelUrl,
                IdempotencyKeyFactory.RenewalCheckout(reservation.IntentId),
                command.Provider,
                command.Method,
                reservation.DiscountCents,
                reservation.DiscountSource,
                reservation.DiscountLabel,
                reservation.Lines
            ),
            ct
        );
        if (checkout.IsFailure)
            return Result.Failure<StartRenewalCheckoutResponse>(checkout.Error);

        var attached = await bus.InvokeAsync<Result>(
            new AttachRenewalCheckoutCommand(
                command.TenantId,
                reservation.IntentId,
                checkout.Value.PaymentId,
                checkout.Value.CheckoutUrl,
                checkout.Value.ExpiresAtUtc,
                command.Provider
            ),
            ct
        );
        if (attached.IsFailure)
            return Result.Failure<StartRenewalCheckoutResponse>(attached.Error);

        return Result.Success(
            new StartRenewalCheckoutResponse(
                reservation.IntentId,
                checkout.Value.CheckoutUrl,
                checkout.Value.PaymentId,
                checkout.Value.ExpiresAtUtc
            )
        );
    }
}
