using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Codes.Domain.Events;
using TaxVision.Codes.Domain.Quotes;
using TaxVision.Codes.Domain.Redemptions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Reservations;

public sealed class CodeReservation : AggregateRoot
{
    public Guid QuoteId { get; private set; }
    public Guid CodeDefinitionId { get; private set; }
    public PaymentReference Payment { get; private set; } = null!;
    public SubjectReference Subject { get; private set; } = null!;
    public Money GrossAmount { get; private set; } = null!;
    public Money DiscountAmount { get; private set; } = null!;
    public Money NetAmount { get; private set; } = null!;
    public SnapshotHash SnapshotHash { get; private set; } = null!;
    public CodeReservationStatus Status { get; private set; }
    public IdempotencyKey ReservationIdempotencyKey { get; private set; } = null!;
    public PayloadFingerprint ReservationPayloadFingerprint { get; private set; } = null!;
    public IdempotencyKey? CommitIdempotencyKey { get; private set; }
    public PayloadFingerprint? CommitPayloadFingerprint { get; private set; }
    public IdempotencyKey? CancellationIdempotencyKey { get; private set; }
    public PayloadFingerprint? CancellationPayloadFingerprint { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid? RedemptionId { get; private set; }
    public Guid? LastCompensationId { get; private set; }
    public Guid? CommitSourceEventId { get; private set; }
    public bool WasLateCommit { get; private set; }
    public bool IsAvailabilityReleased { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? CommittedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime? ExpiredAtUtc { get; private set; }
    public DateTime? CompensatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private CodeReservation() { }

    public static Result<CodeReservation> Create(
        CodeQuote quote,
        PaymentReference payment,
        IdempotencyKey idempotencyKey,
        PayloadFingerprint payloadFingerprint,
        DateTime expiresAtUtc,
        DateTime nowUtc
    )
    {
        var quoteResult = quote.EnsureReservable(nowUtc);
        if (quoteResult.IsFailure)
            return Result.Failure<CodeReservation>(quoteResult.Error);

        if (expiresAtUtc <= nowUtc || expiresAtUtc > quote.ExpiresAtUtc)
            return Result.Failure<CodeReservation>(
                new Error(
                    "Codes.CodeReservation.InvalidExpiry",
                    "Reservation expiry must be after creation and cannot exceed quote expiry."
                )
            );

        var reservation = new CodeReservation
        {
            QuoteId = quote.Id,
            CodeDefinitionId = quote.CodeDefinitionId,
            Payment = payment,
            Subject = quote.Subject,
            GrossAmount = quote.GrossAmount,
            DiscountAmount = quote.DiscountAmount,
            NetAmount = quote.NetAmount,
            SnapshotHash = quote.SnapshotHash,
            Status = CodeReservationStatus.Active,
            ReservationIdempotencyKey = idempotencyKey,
            ReservationPayloadFingerprint = payloadFingerprint,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            UpdatedAtUtc = nowUtc,
        };
        reservation.SetTenant(quote.TenantId);
        reservation.AddDomainEvent(
            new CodeReservationCreatedDomainEvent(
                reservation.Id,
                reservation.TenantId,
                reservation.QuoteId,
                reservation.CodeDefinitionId,
                reservation.Payment,
                nowUtc
            )
        );
        return Result.Success(reservation);
    }

    public Result<CodeRedemption> Commit(
        IdempotencyKey idempotencyKey,
        PayloadFingerprint payloadFingerprint,
        Guid sourceEventId,
        bool allowLateCommit,
        DateTime nowUtc
    )
    {
        var isLateCommit = Status == CodeReservationStatus.Expired;
        if (Status != CodeReservationStatus.Active && !(isLateCommit && allowLateCommit))
            return Result.Failure<CodeRedemption>(
                new Error(
                    "Codes.CodeReservation.InvalidTransition",
                    $"Cannot commit a reservation from status {Status}."
                )
            );

        if (sourceEventId == Guid.Empty)
            return Result.Failure<CodeRedemption>(
                new Error("Codes.CodeReservation.InvalidSourceEvent", "SourceEventId is required.")
            );

        var redemptionResult = CodeRedemption.Create(
            TenantId,
            Id,
            QuoteId,
            CodeDefinitionId,
            Payment,
            GrossAmount,
            DiscountAmount,
            NetAmount,
            SnapshotHash,
            idempotencyKey,
            payloadFingerprint,
            sourceEventId,
            isLateCommit,
            nowUtc
        );
        if (redemptionResult.IsFailure)
            return redemptionResult;

        Status = CodeReservationStatus.Committed;
        CommitIdempotencyKey = idempotencyKey;
        CommitPayloadFingerprint = payloadFingerprint;
        CommitSourceEventId = sourceEventId;
        RedemptionId = redemptionResult.Value.Id;
        WasLateCommit = isLateCommit;
        CommittedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        AddDomainEvent(
            new CodeReservationCommittedDomainEvent(
                Id,
                TenantId,
                redemptionResult.Value.Id,
                Payment,
                GrossAmount,
                DiscountAmount,
                NetAmount,
                isLateCommit,
                nowUtc
            )
        );
        return redemptionResult;
    }

    public Result Cancel(
        IdempotencyKey idempotencyKey,
        PayloadFingerprint payloadFingerprint,
        string reason,
        DateTime nowUtc
    )
    {
        if (Status is not (CodeReservationStatus.Active or CodeReservationStatus.Expired))
            return Result.Failure(
                new Error(
                    "Codes.CodeReservation.InvalidTransition",
                    $"Cannot cancel a reservation from status {Status}."
                )
            );

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Result.Failure(
                new Error(
                    "Codes.CodeReservation.InvalidCancellationReason",
                    "Cancellation reason is required and cannot exceed 500 characters."
                )
            );

        Status = CodeReservationStatus.Cancelled;
        CancellationIdempotencyKey = idempotencyKey;
        CancellationPayloadFingerprint = payloadFingerprint;
        CancellationReason = reason.Trim();
        CancelledAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        AddDomainEvent(
            new CodeReservationCancelledDomainEvent(Id, TenantId, CodeDefinitionId, CancellationReason, nowUtc)
        );
        return Result.Success();
    }

    public Result Expire(DateTime nowUtc)
    {
        if (Status != CodeReservationStatus.Active)
            return Result.Failure(
                new Error(
                    "Codes.CodeReservation.InvalidTransition",
                    $"Cannot expire a reservation from status {Status}."
                )
            );

        if (nowUtc < ExpiresAtUtc)
            return Result.Failure(
                new Error("Codes.CodeReservation.NotDueForExpiry", "Reservation has not reached its expiration time.")
            );

        Status = CodeReservationStatus.Expired;
        ExpiredAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        AddDomainEvent(new CodeReservationExpiredDomainEvent(Id, TenantId, CodeDefinitionId, nowUtc));
        return Result.Success();
    }

    public Result RecordCompensation(Guid compensationId, bool isFinal, DateTime nowUtc)
    {
        if (Status != CodeReservationStatus.Committed)
            return Result.Failure(
                new Error(
                    "Codes.CodeReservation.InvalidTransition",
                    $"Cannot compensate a reservation from status {Status}."
                )
            );

        if (compensationId == Guid.Empty)
            return Result.Failure(
                new Error("Codes.CodeReservation.InvalidCompensation", "CompensationId is required.")
            );

        LastCompensationId = compensationId;
        if (isFinal)
        {
            Status = CodeReservationStatus.Compensated;
            CompensatedAtUtc = nowUtc;
        }
        UpdatedAtUtc = nowUtc;
        AddDomainEvent(
            new CodeReservationCompensatedDomainEvent(
                Id,
                TenantId,
                RedemptionId!.Value,
                compensationId,
                isFinal,
                nowUtc
            )
        );
        return Result.Success();
    }

    public Result MarkAvailabilityReleased(DateTime nowUtc)
    {
        if (Status is not (CodeReservationStatus.Cancelled or CodeReservationStatus.Expired))
            return Result.Failure(
                new Error(
                    "Codes.CodeReservation.AvailabilityNotReleasable",
                    $"Availability cannot be released from status {Status}."
                )
            );

        if (IsAvailabilityReleased)
            return Result.Success();

        IsAvailabilityReleased = true;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public bool IsReservationReplay(IdempotencyKey key, PayloadFingerprint fingerprint) =>
        ReservationIdempotencyKey == key && ReservationPayloadFingerprint == fingerprint;

    public bool IsCommitReplay(IdempotencyKey key, PayloadFingerprint fingerprint) =>
        CommitIdempotencyKey == key && CommitPayloadFingerprint == fingerprint;

    public bool IsCancellationReplay(IdempotencyKey key, PayloadFingerprint fingerprint) =>
        CancellationIdempotencyKey == key && CancellationPayloadFingerprint == fingerprint;
}
