using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;

/// <summary>SUB-12: la clave de idempotencia de la compra de asientos y la repetición de una compra ya hecha.</summary>
internal static class SeatPurchaseReplay
{
    public static Guid? RequestedSeatId(PurchaseSeatsCommand command, int index) =>
        string.IsNullOrWhiteSpace(command.ClientRequestKey)
            ? null
            : PurchaseRequestIds.Seat(command.TenantId, command.ClientRequestKey, index);

    // SUB-12: misma clave, misma compra. Los asientos del lote se confirman juntos: alcanza con ver los bordes.
    public static async Task<Result<IReadOnlyList<Guid>>?> TryReplayAsync(
        PurchaseSeatsCommand command,
        ISubscriptionSeatRepository seats,
        CancellationToken ct
    )
    {
        if (RequestedSeatId(command, 0) is not { } firstId)
            return null;

        var first = await seats.GetByIdAsync(firstId, command.TenantId, ct);
        if (first is null)
            return null;

        var sameRequest =
            command.Quantity >= 1
            && Enum.TryParse<SeatType>(command.SeatType, ignoreCase: true, out var seatType)
            && first.Type == seatType
            && await seats.GetByIdAsync(RequestedSeatId(command, command.Quantity - 1)!.Value, command.TenantId, ct)
                is not null
            && await seats.GetByIdAsync(RequestedSeatId(command, command.Quantity)!.Value, command.TenantId, ct)
                is null;
        if (!sameRequest)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error("Seat.IdempotencyKeyReused", "This Idempotency-Key was already used for another purchase.")
            );

        return Result.Success<IReadOnlyList<Guid>>([
            .. Enumerable.Range(0, command.Quantity).Select(index => RequestedSeatId(command, index)!.Value),
        ]);
    }
}
