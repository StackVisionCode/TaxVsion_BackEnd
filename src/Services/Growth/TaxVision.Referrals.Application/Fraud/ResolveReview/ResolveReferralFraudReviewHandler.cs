using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Attributions;
using TaxVision.Referrals.Domain.Fraud;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Fraud.ResolveReview;

/// <summary>El PlatformAdmin decide una revisión de fraude abierta: aprobar libera el premio; rechazar lo cancela.</summary>
public sealed record ResolveReferralFraudReviewCommand(Guid ReviewId, bool Approve, string Reason, Guid ActorUserId);

/// <param name="RewardTenantId">Oficina dueña del premio, para avisarle si se canceló.</param>
public sealed record ReferralFraudReviewResolution(
    Guid ReviewId,
    FraudReviewStatus Status,
    Guid? RewardCaseId,
    Guid? RewardTenantId,
    bool RewardCancelled
);

public static class ResolveReferralFraudReviewHandler
{
    public static async Task<Result<ReferralFraudReviewResolution>> Handle(
        ResolveReferralFraudReviewCommand command,
        IReferralFraudReviewRepository reviews,
        IReferralAttributionRepository attributions,
        IReferralRewardCaseRepository rewards,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var review = await reviews.GetByIdAsync(command.ReviewId, ct);
        if (review is null)
            return Result.Failure<ReferralFraudReviewResolution>(
                new Error("ReferralFraudReview.NotFound", "The fraud review was not found.")
            );

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var resolved = command.Approve
            ? review.Approve(command.Reason, command.ActorUserId, nowUtc)
            : review.Reject(command.Reason, command.ActorUserId, nowUtc);
        if (resolved.IsFailure)
            return Result.Failure<ReferralFraudReviewResolution>(resolved.Error);

        var attribution = review.AttributionId is { } attributionId
            ? await attributions.GetForReviewAsync(attributionId, ct)
            : null;
        var reward = attribution is null ? null : await rewards.GetByAttributionAsync(attribution.Id, ct);
        var applied = command.Approve
            ? Release(attribution, reward, command, nowUtc)
            : Cancel(attribution, reward, command, nowUtc);
        if (applied.IsFailure)
            return Result.Failure<ReferralFraudReviewResolution>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(
            new ReferralFraudReviewResolution(
                review.Id,
                review.Status,
                reward?.Id,
                reward?.TenantId,
                !command.Approve && reward?.Status == ReferralRewardCaseStatus.Cancelled
            )
        );
    }

    // Visto bueno: la atribución vuelve a su estado y el premio retenido vuelve a esperar su acreditación.
    private static Result Release(
        ReferralAttribution? attribution,
        ReferralRewardCase? reward,
        ResolveReferralFraudReviewCommand command,
        DateTime nowUtc
    )
    {
        if (attribution?.Status == ReferralAttributionStatus.UnderReview)
        {
            var approved = attribution.ApproveFraudReview(command.ActorUserId, nowUtc);
            if (approved.IsFailure)
                return approved;
        }

        return reward?.Status == ReferralRewardCaseStatus.ManualReview && reward.HeldForDisputeId is null
            ? reward.ReleaseFromReview(command.Reason, command.ActorUserId, nowUtc)
            : Result.Success();
    }

    // Rechazo: la atribución queda rechazada y el premio aún no acreditado se cancela.
    private static Result Cancel(
        ReferralAttribution? attribution,
        ReferralRewardCase? reward,
        ResolveReferralFraudReviewCommand command,
        DateTime nowUtc
    )
    {
        if (attribution is not null)
        {
            var rejected = attribution.Reject("FraudReviewRejected", command.ActorUserId, nowUtc);
            if (rejected.IsFailure)
                return rejected;
        }

        return reward?.Status is ReferralRewardCaseStatus.Requested or ReferralRewardCaseStatus.ManualReview
            ? reward.Cancel(command.Reason, command.ActorUserId, nowUtc)
            : Result.Success();
    }
}
