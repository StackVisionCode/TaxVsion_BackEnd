using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Application.Common;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Rewards.ReviewRewardCase;

public enum RewardReviewAction
{
    /// <summary>Retiene el premio (aún no acreditado) para revisarlo.</summary>
    Flag,

    /// <summary>Visto bueno: vuelve a la cola normal y se acredita al vencer su espera.</summary>
    Release,

    /// <summary>Rechazo: el premio se cancela y no se acredita.</summary>
    Reject,
}

/// <summary>
/// Revisión manual de un premio por el PlatformAdmin. El premio pertenece a la oficina referidora: su tenant debe estar
/// en contexto (lo fija el controlador). Toda acción exige un motivo y queda en el caso.
/// </summary>
public sealed record ReviewRewardCaseCommand(
    Guid OwnerTenantId,
    Guid RewardCaseId,
    RewardReviewAction Action,
    string Reason,
    Guid ActorUserId
);

public sealed record ReviewRewardCaseResult(Guid RewardCaseId, ReferralRewardCaseStatus Status);

public static class ReviewRewardCaseHandler
{
    public static async Task<Result<ReviewRewardCaseResult>> Handle(
        ReviewRewardCaseCommand command,
        IReferralRewardCaseRepository rewards,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var actor = ApplicationGuards.EnsureActor(command.ActorUserId);
        if (actor.IsFailure)
            return Result.Failure<ReviewRewardCaseResult>(actor.Error);

        if (!Enum.IsDefined(command.Action))
        {
            return Result.Failure<ReviewRewardCaseResult>(
                new Error("ReferralReward.InvalidAction", "The review action is not supported.")
            );
        }

        var reward = await rewards.GetByIdAsync(command.RewardCaseId, command.OwnerTenantId, ct);
        if (reward is null)
        {
            return Result.Failure<ReviewRewardCaseResult>(
                new Error("ReferralReward.NotFound", "The referral reward does not exist.")
            );
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var changed = command.Action switch
        {
            RewardReviewAction.Flag => reward.FlagForReview(command.Reason, command.ActorUserId, nowUtc),
            RewardReviewAction.Release => reward.ReleaseFromReview(command.Reason, command.ActorUserId, nowUtc),
            _ => reward.Cancel(command.Reason, command.ActorUserId, nowUtc),
        };
        if (changed.IsFailure)
            return Result.Failure<ReviewRewardCaseResult>(changed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new ReviewRewardCaseResult(reward.Id, reward.Status));
    }
}
