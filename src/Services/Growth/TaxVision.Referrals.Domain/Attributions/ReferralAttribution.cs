using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Referrals.Domain.Codes;
using TaxVision.Referrals.Domain.Common;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Domain.Attributions;

public sealed class ReferralAttribution : TenantEntity
{
    public Guid ProgramId { get; private set; }
    public Guid ReferralCodeId { get; private set; }
    public Guid? TenantScopeId { get; private set; }
    public ReferralParticipantType ReferrerType { get; private set; }
    public Guid ReferrerId { get; private set; }
    public ReferralParticipantType RefereeType { get; private set; }
    public Guid RefereeId { get; private set; }
    public ReferralAttributionStatus Status { get; private set; }
    public ReferralAttributionStatus? StatusBeforeReview { get; private set; }
    public DateTime AttributedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? QualifiedAtUtc { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string PayloadFingerprint { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ReferralAttribution() { }

    public static Result<ReferralAttribution> Create(
        ReferralProgram program,
        ReferralCode code,
        ReferralParticipantType refereeType,
        Guid refereeId,
        string idempotencyKey,
        string payloadFingerprint,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var accepting = program.EnsureAcceptingAttributions(nowUtc);
        if (accepting.IsFailure)
            return Result.Failure<ReferralAttribution>(accepting.Error);

        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return Result.Failure<ReferralAttribution>(actor.Error);

        var usableCode = code.EnsureUsable(nowUtc);
        if (usableCode.IsFailure)
            return Result.Failure<ReferralAttribution>(usableCode.Error);

        if (code.ProgramId != program.Id)
        {
            return Result.Failure<ReferralAttribution>(
                new Error("ReferralAttribution.ProgramMismatch", "Referral code does not belong to the program.")
            );
        }

        var expectedParticipant =
            program.FlowType == ReferralFlowType.TenantToTenant
                ? ReferralParticipantType.Tenant
                : ReferralParticipantType.Taxpayer;
        if (refereeType != expectedParticipant)
        {
            return Result.Failure<ReferralAttribution>(
                new Error("ReferralAttribution.InvalidRefereeType", "RefereeType does not match the program flow.")
            );
        }

        if (refereeId == Guid.Empty)
            return Result.Failure<ReferralAttribution>(
                new Error("ReferralAttribution.InvalidReferee", "RefereeId is required.")
            );

        if (code.OwnerType == refereeType && code.OwnerId == refereeId)
        {
            return Result.Failure<ReferralAttribution>(
                new Error("ReferralAttribution.SelfReferral", "Referrer and referee cannot be the same participant.")
            );
        }

        var idempotency = ValidateIdempotency(idempotencyKey, payloadFingerprint);
        if (idempotency.IsFailure)
            return Result.Failure<ReferralAttribution>(idempotency.Error);

        var attribution = new ReferralAttribution
        {
            ProgramId = program.Id,
            ReferralCodeId = code.Id,
            TenantScopeId = program.TenantScopeId,
            ReferrerType = code.OwnerType,
            ReferrerId = code.OwnerId,
            RefereeType = refereeType,
            RefereeId = refereeId,
            Status = ReferralAttributionStatus.Pending,
            AttributedAtUtc = nowUtc,
            ExpiresAtUtc = program.CalculateAttributionExpiry(nowUtc),
            IdempotencyKey = idempotencyKey.Trim(),
            PayloadFingerprint = DomainGuards.NormalizeSha256Hex(payloadFingerprint),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        attribution.SetTenant(program.FlowType == ReferralFlowType.TenantToTenant ? refereeId : program.TenantId);
        return Result.Success(attribution);
    }

    public Result Activate(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status != ReferralAttributionStatus.Pending)
        {
            return Result.Failure(
                new Error("ReferralAttribution.InvalidTransition", $"Cannot activate an attribution from {Status}.")
            );
        }

        if (nowUtc >= ExpiresAtUtc)
            return Result.Failure(new Error("ReferralAttribution.Expired", "The attribution is already expired."));

        Status = ReferralAttributionStatus.Active;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result MarkQualified(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralAttributionStatus.Qualified)
            return Result.Success();

        if (Status != ReferralAttributionStatus.Active)
        {
            return Result.Failure(
                new Error("ReferralAttribution.InvalidTransition", $"Cannot qualify an attribution from {Status}.")
            );
        }

        if (nowUtc >= ExpiresAtUtc)
            return Result.Failure(new Error("ReferralAttribution.Expired", "The attribution has expired."));

        Status = ReferralAttributionStatus.Qualified;
        QualifiedAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result OpenFraudReview(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralAttributionStatus.UnderReview)
            return Result.Success();

        if (Status is not (ReferralAttributionStatus.Active or ReferralAttributionStatus.Qualified))
        {
            return Result.Failure(
                new Error("ReferralAttribution.InvalidTransition", $"Cannot review an attribution from {Status}.")
            );
        }

        StatusBeforeReview = Status;
        Status = ReferralAttributionStatus.UnderReview;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ApproveFraudReview(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status != ReferralAttributionStatus.UnderReview || StatusBeforeReview is null)
        {
            return Result.Failure(
                new Error("ReferralAttribution.InvalidTransition", "The attribution is not under review.")
            );
        }

        Status = StatusBeforeReview.Value;
        StatusBeforeReview = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Reject(string reason, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralAttributionStatus.Rejected)
            return Result.Success();

        if (Status == ReferralAttributionStatus.Expired)
            return Result.Failure(
                new Error("ReferralAttribution.InvalidTransition", "An expired attribution cannot be rejected.")
            );

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("ReferralAttribution.InvalidReason", "A rejection reason is required."));

        Status = ReferralAttributionStatus.Rejected;
        StatusBeforeReview = null;
        RejectedAtUtc = nowUtc;
        RejectionReason = reason.Trim().Length > 500 ? reason.Trim()[..500] : reason.Trim();
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Expire(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralAttributionStatus.Expired)
            return Result.Success();

        if (Status != ReferralAttributionStatus.Active)
        {
            return Result.Failure(
                new Error("ReferralAttribution.InvalidTransition", $"Cannot expire an attribution from {Status}.")
            );
        }

        if (nowUtc < ExpiresAtUtc)
            return Result.Failure(new Error("ReferralAttribution.NotDue", "The attribution is not due to expire."));

        Status = ReferralAttributionStatus.Expired;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private static Result ValidateIdempotency(string key, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 200)
            return Result.Failure(
                new Error("ReferralAttribution.InvalidIdempotencyKey", "A valid idempotency key is required.")
            );

        return !DomainGuards.IsSha256Hex(fingerprint)
            ? Result.Failure(
                new Error(
                    "ReferralAttribution.InvalidPayloadFingerprint",
                    "PayloadFingerprint must be a canonical SHA-256 value encoded as 64 hexadecimal characters."
                )
            )
            : Result.Success();
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
