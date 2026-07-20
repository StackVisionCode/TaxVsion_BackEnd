using TaxVision.Codes.Domain.Redemptions;

namespace TaxVision.Codes.Application.Reservations.CommitReservation;

public sealed record CommitReservationResponse(
    Guid ReservationId,
    Guid RedemptionId,
    Guid CodeDefinitionId,
    string PaymentSource,
    Guid PaymentId,
    long DiscountAmountCents,
    string Currency,
    bool WasLateCommit,
    DateTime CommittedAtUtc
)
{
    public static CommitReservationResponse From(CodeRedemption redemption) =>
        new(
            redemption.ReservationId,
            redemption.Id,
            redemption.CodeDefinitionId,
            redemption.Payment.Source,
            redemption.Payment.PaymentId,
            redemption.DiscountAmount.AmountCents,
            redemption.DiscountAmount.Currency,
            redemption.WasLateCommit,
            redemption.CommittedAtUtc
        );
}
