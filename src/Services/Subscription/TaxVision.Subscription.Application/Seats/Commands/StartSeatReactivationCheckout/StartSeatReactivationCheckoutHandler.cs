using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Subscription.Application.Seats.Commands.StartSeatReactivationCheckout;

/// <summary>
/// Reactivar un seat Suspended/PastDue mediante hosted-checkout: valida estado + tenancy, crea el renewal
/// pendiente en el aggregate con una idempotency key estable (<c>IdempotencyKeyFactory.SeatReactivation</c>),
/// pide a PaymentApp la URL del provider y la devuelve al frontend.
///
/// Diseñado para no multiplicar entidades: NO crea un <c>SeatReactivationIntent</c> dedicado, apoya la unicidad
/// del cobro en (a) la idempotency key, cuyo bucket cambia solo cuando el seat vuelve a caer (ver
/// <c>IdempotencyKeyFactory.SeatReactivation</c>), y (b) el guard del aggregate: <c>BeginReactivation</c> es
/// no-op si ya existe un renewal con esa misma key. El webhook <c>SeatRenewalPaymentSucceededConsumer</c>
/// detecta estado Suspended/PastDue y llama <c>SubscriptionSeat.CompleteReactivation</c> en vez de
/// <c>SubscriptionSeat.CompleteRenewal</c>. SUB-7: orquesta sin transacción; el renewal se confirma antes de
/// llamar a PaymentApp y acá no hay nada que adjuntar.
/// </summary>
public static class StartSeatReactivationCheckoutHandler
{
    [NonTransactional]
    public static async Task<Result<StartSeatReactivationCheckoutResponse>> Handle(
        StartSeatReactivationCheckoutCommand command,
        IMessageBus bus,
        ISeatReactivationCheckoutClient checkoutClient,
        CancellationToken ct
    )
    {
        var begun = await bus.InvokeAsync<Result<SeatReactivationReservation>>(
            new BeginSeatReactivationCommand(command),
            ct
        );
        if (begun.IsFailure)
            return Result.Failure<StartSeatReactivationCheckoutResponse>(begun.Error);

        var checkout = await checkoutClient.CreateReactivationCheckoutAsync(
            new SeatReactivationCheckoutClientRequest(
                command.TenantId,
                command.SeatId,
                begun.Value.AmountCents,
                begun.Value.Currency,
                command.PayerEmail,
                command.SuccessUrl,
                command.CancelUrl,
                begun.Value.IdempotencyKey,
                command.Provider,
                command.Method
            ),
            ct
        );
        if (checkout.IsFailure)
            return Result.Failure<StartSeatReactivationCheckoutResponse>(checkout.Error);

        return Result.Success(
            new StartSeatReactivationCheckoutResponse(
                command.SeatId,
                checkout.Value.CheckoutUrl,
                checkout.Value.PaymentId,
                checkout.Value.ExpiresAtUtc
            )
        );
    }
}
