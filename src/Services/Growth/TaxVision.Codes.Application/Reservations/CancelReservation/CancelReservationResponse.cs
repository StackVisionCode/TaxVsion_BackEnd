using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Codes.Application.Reservations.CancelReservation;

public sealed record CancelReservationResponse(
    Guid ReservationId,
    string Status,
    string Reason,
    DateTime CancelledAtUtc
)
{
    public static CancelReservationResponse From(CodeReservation reservation) =>
        new(
            reservation.Id,
            reservation.Status.ToString(),
            reservation.CancellationReason!,
            reservation.CancelledAtUtc!.Value
        );
}
