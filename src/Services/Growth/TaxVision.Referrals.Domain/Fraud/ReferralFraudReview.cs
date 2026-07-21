using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Referrals.Domain.Common;

namespace TaxVision.Referrals.Domain.Fraud;

/// <summary>
/// Caso de revisión auditable. EvidenceReference apunta a evidencia redacted controlada
/// fuera del aggregate; nunca almacena huellas de pago, dispositivo o PII cruda.
/// </summary>
public sealed class ReferralFraudReview : TenantEntity
{
    public Guid ProgramId { get; private set; }
    public Guid? TenantScopeId { get; private set; }
    public Guid? AttributionId { get; private set; }
    public Guid? RewardCaseId { get; private set; }
    public string SignalCode { get; private set; } = default!;
    public string EvidenceReference { get; private set; } = default!;
    public FraudReviewStatus Status { get; private set; }
    public string? ResolutionReason { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string PayloadFingerprint { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ReferralFraudReview() { }

    public static Result<ReferralFraudReview> Open(
        Guid programId,
        Guid? tenantScopeId,
        Guid ownerTenantId,
        Guid? attributionId,
        Guid? rewardCaseId,
        string signalCode,
        string evidenceReference,
        string idempotencyKey,
        string payloadFingerprint,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (programId == Guid.Empty)
            return Result.Failure<ReferralFraudReview>(
                new Error("FraudReview.InvalidProgram", "ProgramId is required.")
            );

        if (ownerTenantId == Guid.Empty)
            return Result.Failure<ReferralFraudReview>(
                new Error("FraudReview.InvalidOwnerTenant", "OwnerTenantId is required.")
            );

        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return Result.Failure<ReferralFraudReview>(actor.Error);

        if (attributionId is null && rewardCaseId is null)
        {
            return Result.Failure<ReferralFraudReview>(
                new Error("FraudReview.MissingTarget", "AttributionId or RewardCaseId is required.")
            );
        }

        if (string.IsNullOrWhiteSpace(signalCode) || signalCode.Trim().Length > 100)
            return Result.Failure<ReferralFraudReview>(
                new Error("FraudReview.InvalidSignal", "A valid signal code is required.")
            );

        if (string.IsNullOrWhiteSpace(evidenceReference) || evidenceReference.Trim().Length > 500)
        {
            return Result.Failure<ReferralFraudReview>(
                new Error("FraudReview.InvalidEvidenceReference", "A redacted evidence reference is required.")
            );
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
            return Result.Failure<ReferralFraudReview>(
                new Error("FraudReview.InvalidIdempotencyKey", "A valid idempotency key is required.")
            );

        if (!DomainGuards.IsSha256Hex(payloadFingerprint))
        {
            return Result.Failure<ReferralFraudReview>(
                new Error(
                    "FraudReview.InvalidPayloadFingerprint",
                    "PayloadFingerprint must be a canonical SHA-256 value encoded as 64 hexadecimal characters."
                )
            );
        }

        var review = new ReferralFraudReview
        {
            ProgramId = programId,
            TenantScopeId = tenantScopeId,
            AttributionId = attributionId,
            RewardCaseId = rewardCaseId,
            SignalCode = signalCode.Trim(),
            EvidenceReference = evidenceReference.Trim(),
            Status = FraudReviewStatus.Open,
            IdempotencyKey = idempotencyKey.Trim(),
            PayloadFingerprint = DomainGuards.NormalizeSha256Hex(payloadFingerprint),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        review.SetTenant(tenantScopeId ?? ownerTenantId);
        return Result.Success(review);
    }

    public Result BeginInvestigation(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == FraudReviewStatus.Investigating)
            return Result.Success();

        if (Status != FraudReviewStatus.Open)
            return Result.Failure(new Error("FraudReview.InvalidTransition", $"Cannot investigate from {Status}."));

        Status = FraudReviewStatus.Investigating;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Approve(string reason, Guid actorUserId, DateTime nowUtc) =>
        Resolve(FraudReviewStatus.Approved, reason, actorUserId, nowUtc);

    public Result Reject(string reason, Guid actorUserId, DateTime nowUtc) =>
        Resolve(FraudReviewStatus.Rejected, reason, actorUserId, nowUtc);

    public Result Escalate(string reason, Guid actorUserId, DateTime nowUtc) =>
        Resolve(FraudReviewStatus.Escalated, reason, actorUserId, nowUtc);

    private Result Resolve(FraudReviewStatus target, string reason, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == target)
            return Result.Success();

        if (Status is not (FraudReviewStatus.Open or FraudReviewStatus.Investigating))
            return Result.Failure(new Error("FraudReview.InvalidTransition", $"Cannot resolve from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("FraudReview.InvalidReason", "A resolution reason is required."));

        Status = target;
        var trimmed = reason.Trim();
        ResolutionReason = trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
        ResolvedBy = actorUserId;
        ResolvedAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
