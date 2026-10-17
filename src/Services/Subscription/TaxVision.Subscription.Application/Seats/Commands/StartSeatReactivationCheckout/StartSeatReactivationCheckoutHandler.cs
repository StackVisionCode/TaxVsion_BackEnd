using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Seats.Commands.StartSeatReactivationCheckout;

/// <summary>
/// Reactivar un seat Suspended/PastDue mediante hosted-checkout: valida estado + tenancy, crea el renewal
/// pendiente en el aggregate con una idempotency key estable (<see cref="IdempotencyKeyFactory.SeatReactivation"/>),
/// pide a PaymentApp la URL del provider y la devuelve al frontend.
///
/// Diseñado para no multiplicar entidades: NO crea un <c>SeatReactivationIntent</c> dedicado, apoya la unicidad
/// del cobro en (a) la idempotency key, cuyo bucket cambia solo cuando el seat vuelve a caer (ver
/// <c>IdempotencyKeyFactory.SeatReactivation</c>), y (b) el guard del aggregate: <c>BeginReactivation</c> es
/// no-op si ya existe un renewal con esa misma key. El webhook <see cref="SeatRenewalPaymentSucceededConsumer"/>
/// detecta estado Suspended/PastDue y llama <see cref="SubscriptionSeat.CompleteReactivation"/> en vez de
/// <see cref="SubscriptionSeat.CompleteRenewal"/>.
/// </summary>
public static class StartSeatReactivationCheckoutHandler
{
    private static readonly SeatStatus[] ReactivatableStatuses = [SeatStatus.Suspended, SeatStatus.PastDue];

    public static async Task<Result<StartSeatReactivationCheckoutResponse>> Handle(
        StartSeatReactivationCheckoutCommand command,
        ISubscriptionSeatRepository seats,
        ISeatReactivationCheckoutClient checkoutClient,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var seat = await seats.GetByIdAsync(command.SeatId, command.TenantId, ct);
        if (seat is null)
            return Result.Failure<StartSeatReactivationCheckoutResponse>(
                new Error("Seat.NotFound", "Seat does not exist.")
            );

        if (Array.IndexOf(ReactivatableStatuses, seat.Status) < 0)
            return Result.Failure<StartSeatReactivationCheckoutResponse>(
                new Error(
                    "Seat.NotReactivatable",
                    $"Cannot reactivate a seat in status {seat.Status}. Only Suspended or PastDue seats can be reactivated."
                )
            );

        var nowUtc = DateTime.UtcNow;
        var newPeriodEndUtc = seat.BillingCycle.CalculateNext(nowUtc);

        // Monto: 1 período completo al precio histórico del seat. No prorratea — el período empieza en `nowUtc`.
        var unitPriceCents = (long)Math.Round(seat.UnitPrice.Amount * 100m, MidpointRounding.AwayFromZero);
        if (unitPriceCents <= 0)
            return Result.Failure<StartSeatReactivationCheckoutResponse>(
                new Error(
                    "Seat.NothingToCharge",
                    "This seat has no price on record; it cannot be reactivated via checkout."
                )
            );

        var idempotencyKey = IdempotencyKeyFactory.SeatReactivation(seat.Id, seat.SuspendedAtUtc ?? seat.UpdatedAtUtc);

        var begin = seat.BeginReactivation(idempotencyKey, newPeriodEndUtc, command.RequestedByUserId, nowUtc);
        if (begin.IsFailure)
            return Result.Failure<StartSeatReactivationCheckoutResponse>(begin.Error);

        await unitOfWork.SaveChangesAsync(ct);

        var checkout = await checkoutClient.CreateReactivationCheckoutAsync(
            new SeatReactivationCheckoutClientRequest(
                command.TenantId,
                seat.Id,
                unitPriceCents,
                seat.UnitPrice.Currency,
                command.PayerEmail,
                command.SuccessUrl,
                command.CancelUrl,
                idempotencyKey,
                command.Provider,
                command.Method
            ),
            ct
        );
        if (checkout.IsFailure)
            return Result.Failure<StartSeatReactivationCheckoutResponse>(checkout.Error);

        return Result.Success(
            new StartSeatReactivationCheckoutResponse(
                seat.Id,
                checkout.Value.CheckoutUrl,
                checkout.Value.PaymentId,
                checkout.Value.ExpiresAtUtc
            )
        );
    }
}
