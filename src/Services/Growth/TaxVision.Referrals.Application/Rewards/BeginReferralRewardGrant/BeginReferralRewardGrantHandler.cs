using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Rewards.BeginReferralRewardGrant;

public static class BeginReferralRewardGrantHandler
{
    private const string Operation = "BeginReferralRewardGrant";

    public static async Task<Result<ReferralRewardInstruction>> Handle(
        BeginReferralRewardGrantCommand command,
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

        var fingerprint = CanonicalPayloadFingerprint.Compute(command.TenantId, command.RewardCaseId);

        return await idempotency.ExecuteAsync(
            Operation,
            command.RewardCaseId,
            command.IdempotencyKey,
            fingerprint,
            async operationCt =>
            {
                var reward = await rewards.GetByIdAsync(command.RewardCaseId, command.TenantId, operationCt);
                if (reward is null)
                    return NotFound();

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var begin = reward.BeginGrant(command.ActorUserId, nowUtc);
                if (begin.IsFailure)
                    return Result.Failure<ReferralRewardInstruction>(begin.Error);

                var attempt = ReferralRewardAttempt.Start(
                    reward,
                    ReferralRewardOperation.Grant,
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

    private static ReferralRewardInstruction ToInstruction(ReferralRewardCase reward, Guid attemptId) =>
        new(
            reward.Id,
            attemptId,
            reward.GrantId,
            ReferralRewardOperation.Grant,
            reward.BeneficiaryType,
            reward.BeneficiaryId,
            reward.RewardType,
            reward.RewardDefinitionKey
        );

    private static Result<ReferralRewardInstruction> NotFound() =>
        Result.Failure<ReferralRewardInstruction>(
            new Error("ReferralReward.NotFound", "The referral reward does not exist.")
        );
}
