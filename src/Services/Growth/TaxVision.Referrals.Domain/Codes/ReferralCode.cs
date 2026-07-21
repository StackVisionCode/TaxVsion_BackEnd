using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Referrals.Domain.Common;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Domain.Codes;

/// <summary>
/// Identificador de atribución, distinto de un CodeDefinition promocional. Solo conserva
/// hash y fragmentos no sensibles; el token completo se entrega una vez y nunca se persiste.
/// </summary>
public sealed class ReferralCode : TenantEntity
{
    public Guid ProgramId { get; private set; }
    public Guid? TenantScopeId { get; private set; }
    public ReferralParticipantType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public string CodeHash { get; private set; } = default!;
    public string DisplayPrefix { get; private set; } = default!;
    public string LastFour { get; private set; } = default!;
    public ReferralCodeStatus Status { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string PayloadFingerprint { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevocationReason { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private ReferralCode() { }

    public static Result<ReferralCode> Create(
        ReferralProgram program,
        ReferralParticipantType ownerType,
        Guid ownerId,
        string codeHash,
        string displayPrefix,
        string lastFour,
        DateTime expiresAtUtc,
        string idempotencyKey,
        string payloadFingerprint,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var accepting = program.EnsureAcceptingAttributions(nowUtc);
        if (accepting.IsFailure)
            return Result.Failure<ReferralCode>(accepting.Error);

        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return Result.Failure<ReferralCode>(actor.Error);

        var expectedParticipant =
            program.FlowType == ReferralFlowType.TenantToTenant
                ? ReferralParticipantType.Tenant
                : ReferralParticipantType.Taxpayer;
        if (ownerType != expectedParticipant)
        {
            return Result.Failure<ReferralCode>(
                new Error("ReferralCode.InvalidOwnerType", "OwnerType does not match the referral program flow.")
            );
        }

        if (ownerId == Guid.Empty)
            return Result.Failure<ReferralCode>(new Error("ReferralCode.InvalidOwner", "OwnerId is required."));

        if (!DomainGuards.IsSha256Hex(codeHash))
        {
            return Result.Failure<ReferralCode>(
                new Error(
                    "ReferralCode.InvalidHash",
                    "CodeHash must be a SHA-256 value encoded as 64 hexadecimal characters."
                )
            );
        }

        if (string.IsNullOrWhiteSpace(displayPrefix) || displayPrefix.Length > 12)
        {
            return Result.Failure<ReferralCode>(
                new Error("ReferralCode.InvalidPrefix", "DisplayPrefix is required and must be 12 characters or fewer.")
            );
        }

        if (string.IsNullOrWhiteSpace(lastFour) || lastFour.Length != 4)
            return Result.Failure<ReferralCode>(
                new Error("ReferralCode.InvalidLastFour", "LastFour must have 4 characters.")
            );

        if (expiresAtUtc <= nowUtc)
            return Result.Failure<ReferralCode>(
                new Error("ReferralCode.InvalidExpiry", "Expiry must be in the future.")
            );

        if (program.EndsAtUtc is not null && expiresAtUtc > program.EndsAtUtc)
        {
            return Result.Failure<ReferralCode>(
                new Error("ReferralCode.ExceedsProgramPeriod", "Code expiry cannot exceed the program end.")
            );
        }

        var idempotency = ValidateIdempotency(idempotencyKey, payloadFingerprint);
        if (idempotency.IsFailure)
            return Result.Failure<ReferralCode>(idempotency.Error);

        var referralCode = new ReferralCode
        {
            ProgramId = program.Id,
            TenantScopeId = program.TenantScopeId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            CodeHash = DomainGuards.NormalizeSha256Hex(codeHash),
            DisplayPrefix = displayPrefix,
            LastFour = lastFour,
            Status = ReferralCodeStatus.Active,
            ExpiresAtUtc = expiresAtUtc,
            IdempotencyKey = idempotencyKey.Trim(),
            PayloadFingerprint = DomainGuards.NormalizeSha256Hex(payloadFingerprint),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        referralCode.SetTenant(program.FlowType == ReferralFlowType.TenantToTenant ? ownerId : program.TenantId);
        return Result.Success(referralCode);
    }

    public Result EnsureUsable(DateTime nowUtc)
    {
        if (Status != ReferralCodeStatus.Active)
            return Result.Failure(new Error("ReferralCode.NotActive", "The referral code is not active."));

        if (nowUtc >= ExpiresAtUtc)
            return Result.Failure(new Error("ReferralCode.Expired", "The referral code has expired."));

        return Result.Success();
    }

    public Result Revoke(string reason, Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralCodeStatus.Revoked)
            return Result.Success();

        if (Status == ReferralCodeStatus.Expired)
            return Result.Failure(new Error("ReferralCode.InvalidTransition", "An expired code cannot be revoked."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("ReferralCode.InvalidReason", "A revocation reason is required."));

        Status = ReferralCodeStatus.Revoked;
        RevokedAtUtc = nowUtc;
        RevocationReason = reason.Trim().Length > 500 ? reason.Trim()[..500] : reason.Trim();
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Expire(Guid actorUserId, DateTime nowUtc)
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralCodeStatus.Expired)
            return Result.Success();

        if (Status != ReferralCodeStatus.Active)
            return Result.Failure(new Error("ReferralCode.InvalidTransition", $"Cannot expire a code from {Status}."));

        if (nowUtc < ExpiresAtUtc)
            return Result.Failure(new Error("ReferralCode.NotDue", "The referral code has not reached its expiry."));

        Status = ReferralCodeStatus.Expired;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private static Result ValidateIdempotency(string idempotencyKey, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
            return Result.Failure(
                new Error("ReferralCode.InvalidIdempotencyKey", "A valid idempotency key is required.")
            );

        return !DomainGuards.IsSha256Hex(fingerprint)
            ? Result.Failure(
                new Error(
                    "ReferralCode.InvalidPayloadFingerprint",
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
