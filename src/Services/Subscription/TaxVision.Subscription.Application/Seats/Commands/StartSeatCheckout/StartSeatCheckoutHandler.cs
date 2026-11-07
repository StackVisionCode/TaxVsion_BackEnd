using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Subscription.Application.Seats.Commands.StartSeatCheckout;

/// <summary>
/// SUB-7: orquesta sin transacción. La intención se confirma antes de llamar a PaymentApp y la sesión se adjunta
/// después, cada una en su transacción corta. Un reintento encuentra la misma intención y manda la misma clave.
/// </summary>
public static class StartSeatCheckoutHandler
{
    [NonTransactional]
    public static async Task<Result<StartSeatCheckoutResponse>> Handle(
        StartSeatCheckoutCommand command,
        IMessageBus bus,
        ISeatCheckoutPaymentClient checkoutClient,
        CancellationToken ct
    )
    {
        var reserved = await bus.InvokeAsync<Result<SeatCheckoutReservation>>(
            new ReserveSeatCheckoutCommand(command),
            ct
        );
        if (reserved.IsFailure)
            return Result.Failure<StartSeatCheckoutResponse>(reserved.Error);

        var reservation = reserved.Value;
        if (reservation.OpenCheckout is { } open)
            return Result.Success(open);

        var checkout = await checkoutClient.CreateCheckoutAsync(
            new SeatCheckoutClientRequest(
                command.TenantId,
                reservation.IntentId,
                reservation.TotalCents,
                reservation.Currency,
                command.PayerEmail,
                command.SuccessUrl,
                command.CancelUrl,
                IdempotencyKeyFactory.SeatCheckout(reservation.IntentId),
                command.Provider,
                command.Method,
                reservation.Quantity,
                ProratedUnit.Of(reservation.TotalCents, reservation.Quantity)
            ),
            ct
        );
        if (checkout.IsFailure)
            return Result.Failure<StartSeatCheckoutResponse>(checkout.Error);

        var attached = await bus.InvokeAsync<Result>(
            new AttachSeatCheckoutCommand(
                command.TenantId,
                reservation.IntentId,
                checkout.Value.PaymentId,
                checkout.Value.CheckoutUrl,
                checkout.Value.ExpiresAtUtc
            ),
            ct
        );
        if (attached.IsFailure)
            return Result.Failure<StartSeatCheckoutResponse>(attached.Error);

        return Result.Success(
            new StartSeatCheckoutResponse(
                reservation.IntentId,
                checkout.Value.CheckoutUrl,
                checkout.Value.PaymentId,
                checkout.Value.ExpiresAtUtc
            )
        );
    }
}
