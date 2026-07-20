using TaxVision.Codes.Domain.Compensations;

namespace TaxVision.Codes.Application.Compensations.CompensateRedemption;

public sealed record CompensateRedemptionResponse(
    Guid CompensationId,
    Guid RedemptionId,
    Guid ReservationId,
    string Type,
    long AdjustmentAmountCents,
    long CumulativeAdjustmentAmountCents,
    string Currency,
    bool IsFinal,
    DateTime CreatedAtUtc
)
{
    public static CompensateRedemptionResponse From(CodeCompensation compensation) =>
        new(
            compensation.Id,
            compensation.RedemptionId,
            compensation.ReservationId,
            compensation.Type.ToString(),
            compensation.AdjustmentAmount.AmountCents,
            compensation.CumulativeAdjustmentAmountCents,
            compensation.AdjustmentAmount.Currency,
            compensation.IsFinal,
            compensation.CreatedAtUtc
        );
}
