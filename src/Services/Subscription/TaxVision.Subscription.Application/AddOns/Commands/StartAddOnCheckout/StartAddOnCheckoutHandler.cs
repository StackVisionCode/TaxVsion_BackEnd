using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;

/// <summary>
/// Abre (o retoma) el checkout hosteado para comprar un add-on. Molde: <c>StartSeatCheckoutHandler</c>. El
/// <c>TenantAddOn</c> NO se crea acá: solo la intención. Lo activa el consumer cuando el pago se confirma, así
/// nadie queda con un add-on sin haber pagado. SUB-7: orquesta sin transacción, igual que el molde.
/// </summary>
public static class StartAddOnCheckoutHandler
{
    [NonTransactional]
    public static async Task<Result<StartAddOnCheckoutResponse>> Handle(
        StartAddOnCheckoutCommand command,
        IMessageBus bus,
        IAddOnCheckoutPaymentClient checkoutClient,
        CancellationToken ct
    )
    {
        var reserved = await bus.InvokeAsync<Result<AddOnCheckoutReservation>>(
            new ReserveAddOnCheckoutCommand(command),
            ct
        );
        if (reserved.IsFailure)
            return Result.Failure<StartAddOnCheckoutResponse>(reserved.Error);

        var reservation = reserved.Value;
        if (reservation.OpenCheckout is { } open)
            return Result.Success(open);

        var checkout = await checkoutClient.CreateCheckoutAsync(
            new AddOnCheckoutClientRequest(
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
            ),
            ct
        );
        if (checkout.IsFailure)
            return Result.Failure<StartAddOnCheckoutResponse>(checkout.Error);

        var attached = await bus.InvokeAsync<Result>(
            new AttachAddOnCheckoutCommand(
                command.TenantId,
                reservation.IntentId,
                checkout.Value.PaymentId,
                checkout.Value.CheckoutUrl,
                checkout.Value.ExpiresAtUtc
            ),
            ct
        );
        if (attached.IsFailure)
            return Result.Failure<StartAddOnCheckoutResponse>(attached.Error);

        return Result.Success(
            new StartAddOnCheckoutResponse(
                reservation.IntentId,
                checkout.Value.CheckoutUrl,
                checkout.Value.PaymentId,
                checkout.Value.ExpiresAtUtc
            )
        );
    }
}
