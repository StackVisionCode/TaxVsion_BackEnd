using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Codes.Application.Reservations.ReserveCode;

public sealed record ReserveCodeResponse(
    Guid ReservationId,
    Guid QuoteId,
    Guid CodeDefinitionId,
    string Status,
    long GrossAmountCents,
    long DiscountAmountCents,
    long NetAmountCents,
    string Currency,
    DateTime ExpiresAtUtc
)
{
    public static ReserveCodeResponse From(CodeReservation reservation) =>
        new(
            reservation.Id,
            reservation.QuoteId,
            reservation.CodeDefinitionId,
            reservation.Status.ToString(),
            reservation.GrossAmount.AmountCents,
            reservation.DiscountAmount.AmountCents,
            reservation.NetAmount.AmountCents,
            reservation.NetAmount.Currency,
            reservation.ExpiresAtUtc
        );
}
