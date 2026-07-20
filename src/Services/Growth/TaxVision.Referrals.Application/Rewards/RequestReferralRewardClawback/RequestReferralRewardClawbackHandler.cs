using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Rewards.RequestReferralRewardClawback;

public static class RequestReferralRewardClawbackHandler
{
    private const string Operation = "RequestReferralRewardClawback";

    public static async Task<Result<ReferralRewardInstruction>> Handle(
        RequestReferralRewardClawbackCommand command,
        IReferralRewardCaseRepository rewards,
        IReferralRewardAttemptRepository attempts,
        IReferralIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<ReferralRewardInstruction>(actor.Error);

        var fingerprint = CanonicalPayloadFingerprint.Compute(
            command.RewardCaseId,
            command.SourceEventId,
            command.Reason
        );
        var businessKey = $"event:{command.SourceEventId:N}";

        return await idempotency.ExecuteAsync(
            Operation,
            command.RewardCaseId,
            businessKey,
            fingerprint,
            async operationCt =>
            {
                var reward = await rewards.GetForCompensationAsync(
                    command.RewardCaseId,
                    operationCt
                );
                if (reward is null)
                {
                    return Result.Failure<ReferralRewardInstruction>(
                        new Error(
                            "ReferralReward.NotFound",
                            "The referral reward does not exist."
                        )
                    );
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var clawback = reward.RequestClawback(
                    command.Reason,
                    command.ActorUserId,
                    nowUtc
                );
                if (clawback.IsFailure)
                    return Result.Failure<ReferralRewardInstruction>(clawback.Error);

                var attempt = ReferralRewardAttempt.Start(
                    reward,
                    ReferralRewardOperation.Clawback,
                    command.IdempotencyKey,
                    fingerprint,
                    command.ActorUserId,
                    nowUtc
                );
                if (attempt.IsFailure)
                    return Result.Failure<ReferralRewardInstruction>(attempt.Error);

                await attempts.AddAsync(attempt.Value, operationCt);
                return Result.Success(ToInstruction(reward, attempt.Value.Id));
            },
            ct
        );
    }

    private static ReferralRewardInstruction ToInstruction(
        ReferralRewardCase reward,
        Guid attemptId
    ) =>
        new(
            reward.Id,
            attemptId,
            reward.GrantId,
            ReferralRewardOperation.Clawback,
            reward.BeneficiaryType,
            reward.BeneficiaryId,
            reward.RewardType,
            reward.RewardDefinitionKey
        );
}
