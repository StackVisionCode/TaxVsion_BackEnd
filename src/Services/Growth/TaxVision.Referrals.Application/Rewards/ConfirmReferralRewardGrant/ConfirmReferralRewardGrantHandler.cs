using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;

namespace TaxVision.Referrals.Application.Rewards.ConfirmReferralRewardGrant;

public static class ConfirmReferralRewardGrantHandler
{
    private const string Operation = "ConfirmReferralRewardGrant";

    public static async Task<Result> Handle(
        ConfirmReferralRewardGrantCommand command,
        IReferralRewardCaseRepository rewards,
        IReferralRewardAttemptRepository attempts,
        IReferralIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return actor;

        var fingerprint = CanonicalPayloadFingerprint.Compute(
            command.GrantId,
            command.AttemptId,
            command.MaterializedBenefitReference
        );
        var executed = await idempotency.ExecuteAsync(
            Operation,
            command.GrantId,
            command.IdempotencyKey,
            fingerprint,
            async operationCt =>
            {
                var reward = await rewards.GetByGrantIdAsync(command.GrantId, command.TenantId, operationCt);
                var attempt = await attempts.GetByIdAsync(command.AttemptId, command.TenantId, operationCt);
                if (reward is null || attempt is null || attempt.RewardCaseId != reward.Id)
                {
                    return Result.Failure<ReferralOperationReceipt>(
                        new Error("ReferralReward.NotFound", "The referral reward does not exist.")
                    );
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var attemptResult = attempt.MarkSucceeded(
                    command.MaterializedBenefitReference,
                    command.IdempotencyKey,
                    fingerprint,
                    command.ActorUserId,
                    nowUtc
                );
                if (attemptResult.IsFailure)
                    return Result.Failure<ReferralOperationReceipt>(attemptResult.Error);

                var rewardResult = reward.ConfirmGranted(
                    command.MaterializedBenefitReference,
                    command.ActorUserId,
                    nowUtc
                );
                if (rewardResult.IsFailure)
                    return Result.Failure<ReferralOperationReceipt>(rewardResult.Error);

                return Result.Success(new ReferralOperationReceipt(reward.Id, attempt.Id, reward.Status.ToString()));
            },
            ct
        );

        return executed.IsSuccess ? Result.Success() : Result.Failure(executed.Error);
    }
}
