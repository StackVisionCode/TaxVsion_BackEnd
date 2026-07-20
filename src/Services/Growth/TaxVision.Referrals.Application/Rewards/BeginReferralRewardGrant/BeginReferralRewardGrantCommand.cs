namespace TaxVision.Referrals.Application.Rewards.BeginReferralRewardGrant;

public sealed record BeginReferralRewardGrantCommand(
    Guid TenantId,
    Guid RewardCaseId,
    string IdempotencyKey,
    Guid ActorUserId
);
