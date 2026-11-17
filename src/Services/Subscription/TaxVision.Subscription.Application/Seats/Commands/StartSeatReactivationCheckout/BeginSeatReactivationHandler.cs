using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Seats.Commands.StartSeatReactivationCheckout;

/// <summary>Paso 1 de SUB-7: confirma el renewal pendiente antes de llamar a PaymentApp.</summary>
public sealed record BeginSeatReactivationCommand(StartSeatReactivationCheckoutCommand Request)
{
    public Guid TenantId => Request.TenantId;
}

public sealed record SeatReactivationReservation(long AmountCents, string Currency, string IdempotencyKey);

public static class BeginSeatReactivationHandler
{
    private static readonly SeatStatus[] ReactivatableStatuses = [SeatStatus.Suspended, SeatStatus.PastDue];

    public static async Task<Result<SeatReactivationReservation>> Handle(
        BeginSeatReactivationCommand reservation,
        ISubscriptionSeatRepository seats,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IOptions<SeatReactivationOptions> options,
        CancellationToken ct
    )
    {
        var command = reservation.Request;
        var seat = await seats.GetByIdAsync(command.SeatId, command.TenantId, ct);
        if (seat is null)
            return Result.Failure<SeatReactivationReservation>(new Error("Seat.NotFound", "Seat does not exist."));

        if (Array.IndexOf(ReactivatableStatuses, seat.Status) < 0)
            return Result.Failure<SeatReactivationReservation>(
                new Error(
                    "Seat.NotReactivatable",
                    $"Cannot reactivate a seat in status {seat.Status}. Only Suspended or PastDue seats can be reactivated."
                )
            );

        var nowUtc = DateTime.UtcNow;
        var unitPriceCents = (long)Math.Round(seat.UnitPrice.Amount * 100m, MidpointRounding.AwayFromZero);
        if (unitPriceCents <= 0)
            return Result.Failure<SeatReactivationReservation>(
                new Error(
                    "Seat.NothingToCharge",
                    "This seat has no price on record; it cannot be reactivated via checkout."
                )
            );

        // SUB-14: empezar toca UpdatedAtUtc, así que el clic siguiente tiene que reusar la clave ya confirmada.
        // Con otro proveedor que el de la sesión abierta, esa se deja y se abre otra con su propia clave.
        DropOpenReactivationOfAnotherProvider(seat, command.Provider, nowUtc - options.Value.OpenSessionWindow);
        var idempotencyKey =
            OpenReactivationKey(seat, nowUtc - options.Value.OpenSessionWindow) ?? NewReactivationKey(seat, nowUtc);

        // D-A: termina con el plan activo y cobra ese tramo a su precio. Un clic repetido cobra el período ya programado.
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        var planEndUtc = subscription is { Status: SubscriptionStatus.Active }
            ? subscription.CurrentPeriodEndUtc
            : (DateTime?)null;
        var scheduled = seat.Renewals.FirstOrDefault(renewal => renewal.IdempotencyKey == idempotencyKey);
        var period = scheduled is null
            ? SeatAlignment.ForReactivation(seat.UnitPrice, seat.BillingCycle, nowUtc, planEndUtc)
            : SeatAlignment.ForReactivation(
                seat.UnitPrice,
                seat.BillingCycle,
                scheduled.PeriodStartUtc,
                scheduled.PeriodEndUtc
            );

        var begin = seat.BeginReactivation(
            idempotencyKey,
            period.PeriodEndUtc,
            command.RequestedByUserId,
            nowUtc,
            command.Provider
        );
        if (begin.IsFailure)
            return Result.Failure<SeatReactivationReservation>(begin.Error);

        await unitOfWork.SaveChangesAsync(ct);

        var amountCents = (long)Math.Round(period.Price.Amount * 100m, MidpointRounding.AwayFromZero);
        return Result.Success(new SeatReactivationReservation(amountCents, seat.UnitPrice.Currency, idempotencyKey));
    }

    // SUB-16: la clave de un Suspended es fija. Ya usada y fuera de plazo, repetirla devolvería la URL vencida.
    private static string NewReactivationKey(SubscriptionSeat seat, DateTime nowUtc)
    {
        var key = IdempotencyKeyFactory.SeatReactivation(seat.Id, seat.SuspendedAtUtc ?? seat.UpdatedAtUtc);
        return seat.Renewals.Any(renewal => renewal.IdempotencyKey == key)
            ? IdempotencyKeyFactory.SeatReactivation(seat.Id, nowUtc)
            : key;
    }

    private static void DropOpenReactivationOfAnotherProvider(
        SubscriptionSeat seat,
        string provider,
        DateTime openSinceUtc
    )
    {
        foreach (var renewal in seat.Renewals.Where(renewal => IsOpenReactivation(renewal, openSinceUtc)))
        {
            if (!string.Equals(renewal.CheckoutProvider, provider, StringComparison.OrdinalIgnoreCase))
                renewal.CancelAutomaticCharge();
        }
    }

    private static bool IsOpenReactivation(SubscriptionSeatRenewal renewal, DateTime openSinceUtc) =>
        renewal.Status == RenewalStatus.Scheduled
        && renewal.ScheduledAtUtc >= openSinceUtc
        && IdempotencyKeyFactory.IsSeatReactivation(renewal.IdempotencyKey);

    private static string? OpenReactivationKey(SubscriptionSeat seat, DateTime openSinceUtc) =>
        seat
            .Renewals.Where(renewal =>
                renewal.Status == RenewalStatus.Scheduled
                && renewal.ScheduledAtUtc >= openSinceUtc
                && IdempotencyKeyFactory.IsSeatReactivation(renewal.IdempotencyKey)
            )
            .OrderByDescending(renewal => renewal.ScheduledAtUtc)
            .FirstOrDefault()
            ?.IdempotencyKey;
}
