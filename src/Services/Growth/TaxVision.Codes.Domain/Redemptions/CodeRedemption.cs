using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Redemptions;

public sealed class CodeRedemption : TenantEntity
{
    public Guid ReservationId { get; private set; }
    public Guid QuoteId { get; private set; }
    public Guid CodeDefinitionId { get; private set; }
    public PaymentReference Payment { get; private set; } = null!;
    public Money GrossAmount { get; private set; } = null!;
    public Money DiscountAmount { get; private set; } = null!;
    public Money NetAmount { get; private set; } = null!;
    public SnapshotHash SnapshotHash { get; private set; } = null!;
    public IdempotencyKey CommitIdempotencyKey { get; private set; } = null!;
    public PayloadFingerprint CommitPayloadFingerprint { get; private set; } = null!;
    public Guid SourceEventId { get; private set; }
    public bool WasLateCommit { get; private set; }
    public DateTime CommittedAtUtc { get; private set; }

    private CodeRedemption() { }

    internal static Result<CodeRedemption> Create(
        Guid tenantId,
        Guid reservationId,
        Guid quoteId,
        Guid codeDefinitionId,
        PaymentReference payment,
        Money grossAmount,
        Money discountAmount,
        Money netAmount,
        SnapshotHash snapshotHash,
        IdempotencyKey idempotencyKey,
        PayloadFingerprint payloadFingerprint,
        Guid sourceEventId,
        bool wasLateCommit,
        DateTime committedAtUtc
    )
    {
        if (
            tenantId == Guid.Empty
            || reservationId == Guid.Empty
            || quoteId == Guid.Empty
            || codeDefinitionId == Guid.Empty
            || sourceEventId == Guid.Empty
        )
            return Result.Failure<CodeRedemption>(
                new Error(
                    "Codes.CodeRedemption.InvalidReference",
                    "Tenant, reservation, quote, code definition, and source event are required."
                )
            );

        if (
            !string.Equals(grossAmount.Currency, discountAmount.Currency, StringComparison.Ordinal)
            || !string.Equals(grossAmount.Currency, netAmount.Currency, StringComparison.Ordinal)
            || grossAmount.AmountCents - discountAmount.AmountCents != netAmount.AmountCents
        )
            return Result.Failure<CodeRedemption>(
                new Error("Codes.CodeRedemption.InvalidAmounts", "Redemption monetary snapshot is inconsistent.")
            );

        var redemption = new CodeRedemption
        {
            ReservationId = reservationId,
            QuoteId = quoteId,
            CodeDefinitionId = codeDefinitionId,
            Payment = payment,
            GrossAmount = grossAmount,
            DiscountAmount = discountAmount,
            NetAmount = netAmount,
            SnapshotHash = snapshotHash,
            CommitIdempotencyKey = idempotencyKey,
            CommitPayloadFingerprint = payloadFingerprint,
            SourceEventId = sourceEventId,
            WasLateCommit = wasLateCommit,
            CommittedAtUtc = committedAtUtc,
        };
        redemption.SetTenant(tenantId);
        return Result.Success(redemption);
    }
}
