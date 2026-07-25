namespace TaxVision.Referrals.Application.Rewards.RequestReferralRewardClawback;

public sealed record RequestReferralRewardClawbackCommand(
    Guid TenantId,
    Guid RewardCaseId,
    Guid SourceEventId,
    string Reason,
    string IdempotencyKey,
    Guid ActorUserId
);
