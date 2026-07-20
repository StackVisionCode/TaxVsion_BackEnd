using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Codes.Domain.Redemptions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Compensations;

public sealed class CodeCompensation : TenantEntity
{
    public Guid RedemptionId { get; private set; }
    public Guid ReservationId { get; private set; }
    public Guid CodeDefinitionId { get; private set; }
    public CodeCompensationType Type { get; private set; }
    public Money AdjustmentAmount { get; private set; } = null!;
    public long CumulativeAdjustmentAmountCents { get; private set; }
    public bool IsFinal { get; private set; }
    public string Reason { get; private set; } = default!;
    public Guid SourceEventId { get; private set; }
    public IdempotencyKey IdempotencyKey { get; private set; } = null!;
    public PayloadFingerprint PayloadFingerprint { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }

    private CodeCompensation() { }

    public static Result<CodeCompensation> Create(
        CodeRedemption redemption,
        CodeCompensationType type,
        Money adjustmentAmount,
        long priorCumulativeAdjustmentAmountCents,
        string reason,
        Guid sourceEventId,
        IdempotencyKey idempotencyKey,
        PayloadFingerprint payloadFingerprint,
        DateTime nowUtc
    )
    {
        if (!Enum.IsDefined(type))
            return Result.Failure<CodeCompensation>(
                new Error("Codes.CodeCompensation.InvalidType", "Compensation type is invalid.")
            );

        if (
            !string.Equals(adjustmentAmount.Currency, redemption.DiscountAmount.Currency, StringComparison.Ordinal)
            || adjustmentAmount.AmountCents > redemption.DiscountAmount.AmountCents
        )
            return Result.Failure<CodeCompensation>(
                new Error(
                    "Codes.CodeCompensation.InvalidAmount",
                    "Adjustment must use the redemption currency and cannot exceed its discount."
                )
            );

        if (priorCumulativeAdjustmentAmountCents < 0)
            return Result.Failure<CodeCompensation>(
                new Error(
                    "Codes.CodeCompensation.InvalidCumulativeAmount",
                    "Prior cumulative adjustment cannot be negative."
                )
            );

        long cumulativeAdjustmentAmountCents;
        try
        {
            cumulativeAdjustmentAmountCents = checked(
                priorCumulativeAdjustmentAmountCents + adjustmentAmount.AmountCents
            );
        }
        catch (OverflowException)
        {
            return Result.Failure<CodeCompensation>(
                new Error("Codes.CodeCompensation.AmountOverflow", "Cumulative adjustment amount overflowed.")
            );
        }

        if (cumulativeAdjustmentAmountCents > redemption.DiscountAmount.AmountCents)
            return Result.Failure<CodeCompensation>(
                new Error(
                    "Codes.CodeCompensation.CumulativeAmountExceeded",
                    "Cumulative adjustments cannot exceed the original discount."
                )
            );

        if (type == CodeCompensationType.KeepConsumed && adjustmentAmount.AmountCents != 0)
            return Result.Failure<CodeCompensation>(
                new Error(
                    "Codes.CodeCompensation.KeepConsumedAmount",
                    "KeepConsumed must not adjust the discount amount."
                )
            );

        if (type == CodeCompensationType.ProportionalAdjustment && adjustmentAmount.AmountCents <= 0)
            return Result.Failure<CodeCompensation>(
                new Error(
                    "Codes.CodeCompensation.ProportionalAmountRequired",
                    "A proportional adjustment must be greater than zero."
                )
            );

        if (
            type == CodeCompensationType.RestoreAvailability
            && cumulativeAdjustmentAmountCents != redemption.DiscountAmount.AmountCents
        )
            return Result.Failure<CodeCompensation>(
                new Error(
                    "Codes.CodeCompensation.FullRestoreRequired",
                    "Restoring availability requires the full discount to be adjusted."
                )
            );

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Result.Failure<CodeCompensation>(
                new Error(
                    "Codes.CodeCompensation.InvalidReason",
                    "Reason is required and cannot exceed 500 characters."
                )
            );

        if (sourceEventId == Guid.Empty)
            return Result.Failure<CodeCompensation>(
                new Error("Codes.CodeCompensation.InvalidSourceEvent", "SourceEventId is required.")
            );

        var isFinal =
            type is CodeCompensationType.RestoreAvailability or CodeCompensationType.RevokeBenefit
            || redemption.DiscountAmount.AmountCents > 0
                && cumulativeAdjustmentAmountCents == redemption.DiscountAmount.AmountCents;

        var compensation = new CodeCompensation
        {
            RedemptionId = redemption.Id,
            ReservationId = redemption.ReservationId,
            CodeDefinitionId = redemption.CodeDefinitionId,
            Type = type,
            AdjustmentAmount = adjustmentAmount,
            CumulativeAdjustmentAmountCents = cumulativeAdjustmentAmountCents,
            IsFinal = isFinal,
            Reason = reason.Trim(),
            SourceEventId = sourceEventId,
            IdempotencyKey = idempotencyKey,
            PayloadFingerprint = payloadFingerprint,
            CreatedAtUtc = nowUtc,
        };
        compensation.SetTenant(redemption.TenantId);
        return Result.Success(compensation);
    }
}
