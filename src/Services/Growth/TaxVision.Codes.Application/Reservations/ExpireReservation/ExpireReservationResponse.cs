using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Codes.Application.Reservations.ExpireReservation;

public sealed record ExpireReservationResponse(
    Guid ReservationId,
    string Status,
    bool IsAvailabilityReleased,
    DateTime? ExpiredAtUtc
)
{
    public static ExpireReservationResponse From(CodeReservation reservation) =>
        new(
            reservation.Id,
            reservation.Status.ToString(),
            reservation.IsAvailabilityReleased,
            reservation.ExpiredAtUtc
        );
}
