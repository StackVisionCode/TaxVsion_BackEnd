using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Referrals.Domain.Common;

namespace TaxVision.Referrals.Domain.Rewards;

public sealed class ReferralRewardAttempt : TenantEntity
{
    public Guid RewardCaseId { get; private set; }
    public Guid? TenantScopeId { get; private set; }
    public ReferralRewardOperation Operation { get; private set; }
    public ReferralRewardAttemptStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string PayloadFingerprint { get; private set; } = default!;
    public string? ExternalReference { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }
    public string? CompletionIdempotencyKey { get; private set; }
    public string? CompletionPayloadFingerprint { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }

    private ReferralRewardAttempt() { }

    public static Result<ReferralRewardAttempt> Start(
        ReferralRewardCase rewardCase,
        ReferralRewardOperation operation,
        string idempotencyKey,
        string payloadFingerprint,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (!Enum.IsDefined(operation))
        {
            return Result.Failure<ReferralRewardAttempt>(
                new Error(
                    "ReferralRewardAttempt.InvalidOperation",
                    "Reward operation is not supported."
                )
            );
        }

        var expectedStatus = operation == ReferralRewardOperation.Grant
            ? ReferralRewardCaseStatus.PendingGrant
            : ReferralRewardCaseStatus.ClawbackPending;
        if (rewardCase.Status != expectedStatus)
        {
            return Result.Failure<ReferralRewardAttempt>(
                new Error(
                    "ReferralRewardAttempt.InvalidCaseState",
                    $"A {operation} attempt requires reward case state {expectedStatus}."
                )
            );
        }

        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return Result.Failure<ReferralRewardAttempt>(actor.Error);

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
            return Result.Failure<ReferralRewardAttempt>(new Error("ReferralRewardAttempt.InvalidIdempotencyKey", "A valid idempotency key is required."));

        if (!DomainGuards.IsSha256Hex(payloadFingerprint))
        {
            return Result.Failure<ReferralRewardAttempt>(
                new Error(
                    "ReferralRewardAttempt.InvalidPayloadFingerprint",
                    "PayloadFingerprint must be a canonical SHA-256 value encoded as 64 hexadecimal characters."
                )
            );
        }

        var attempt = new ReferralRewardAttempt
            {
                RewardCaseId = rewardCase.Id,
                TenantScopeId = rewardCase.TenantScopeId,
                Operation = operation,
                Status = ReferralRewardAttemptStatus.Pending,
                IdempotencyKey = idempotencyKey.Trim(),
                PayloadFingerprint = DomainGuards.NormalizeSha256Hex(payloadFingerprint),
                CreatedAtUtc = nowUtc,
                CreatedBy = actorUserId,
                UpdatedBy = actorUserId,
            };
        attempt.SetTenant(rewardCase.TenantId);
        return Result.Success(attempt);
    }

    public Result MarkSucceeded(
        string externalReference,
        string completionIdempotencyKey,
        string completionPayloadFingerprint,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralRewardAttemptStatus.Succeeded)
        {
            return ExternalReference == externalReference
                    && CompletionIdempotencyKey == completionIdempotencyKey.Trim()
                    && CompletionPayloadFingerprint == completionPayloadFingerprint.ToLowerInvariant()
                ? Result.Success()
                : Result.Failure(
                    new Error(
                        "ReferralRewardAttempt.IdempotencyConflict",
                        "The completed attempt was replayed with a different payload."
                    )
                );
        }

        if (Status != ReferralRewardAttemptStatus.Pending)
            return Result.Failure(new Error("ReferralRewardAttempt.InvalidTransition", $"Cannot succeed from {Status}."));

        if (string.IsNullOrWhiteSpace(externalReference) || externalReference.Length > 200)
            return Result.Failure(new Error("ReferralRewardAttempt.InvalidExternalReference", "A valid external reference is required."));

        var completion = ValidateCompletionIdempotency(
            completionIdempotencyKey,
            completionPayloadFingerprint
        );
        if (completion.IsFailure)
            return completion;

        Status = ReferralRewardAttemptStatus.Succeeded;
        ExternalReference = externalReference.Trim();
        CompletionIdempotencyKey = completionIdempotencyKey.Trim();
        CompletionPayloadFingerprint = DomainGuards.NormalizeSha256Hex(completionPayloadFingerprint);
        CompletedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
        return Result.Success();
    }

    public Result MarkFailed(
        string failureCode,
        string failureReason,
        string completionIdempotencyKey,
        string completionPayloadFingerprint,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var actor = DomainGuards.EnsureActor(actorUserId);
        if (actor.IsFailure)
            return actor;

        if (Status == ReferralRewardAttemptStatus.Failed)
        {
            return FailureCode == failureCode
                    && CompletionIdempotencyKey == completionIdempotencyKey.Trim()
                    && CompletionPayloadFingerprint == completionPayloadFingerprint.ToLowerInvariant()
                ? Result.Success()
                : Result.Failure(
                    new Error(
                        "ReferralRewardAttempt.IdempotencyConflict",
                        "The completed attempt was replayed with a different payload."
                    )
                );
        }

        if (Status != ReferralRewardAttemptStatus.Pending)
            return Result.Failure(new Error("ReferralRewardAttempt.InvalidTransition", $"Cannot fail from {Status}."));

        if (string.IsNullOrWhiteSpace(failureCode))
            return Result.Failure(new Error("ReferralRewardAttempt.InvalidFailureCode", "FailureCode is required."));

        var completion = ValidateCompletionIdempotency(
            completionIdempotencyKey,
            completionPayloadFingerprint
        );
        if (completion.IsFailure)
            return completion;

        Status = ReferralRewardAttemptStatus.Failed;
        FailureCode = failureCode.Trim().Length > 100 ? failureCode.Trim()[..100] : failureCode.Trim();
        var reason = failureReason.Trim();
        FailureReason = reason.Length > 500 ? reason[..500] : reason;
        CompletionIdempotencyKey = completionIdempotencyKey.Trim();
        CompletionPayloadFingerprint = DomainGuards.NormalizeSha256Hex(completionPayloadFingerprint);
        CompletedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
        return Result.Success();
    }

    private static Result ValidateCompletionIdempotency(string key, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 200)
        {
            return Result.Failure(
                new Error(
                    "ReferralRewardAttempt.InvalidCompletionIdempotencyKey",
                    "A valid completion idempotency key is required."
                )
            );
        }

        return !DomainGuards.IsSha256Hex(fingerprint)
            ? Result.Failure(
                new Error(
                    "ReferralRewardAttempt.InvalidCompletionPayloadFingerprint",
                    "CompletionPayloadFingerprint must be a canonical SHA-256 value encoded as 64 hexadecimal characters."
                )
            )
            : Result.Success();
    }
}
