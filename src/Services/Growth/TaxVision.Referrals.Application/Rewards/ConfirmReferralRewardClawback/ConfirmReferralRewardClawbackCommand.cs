namespace TaxVision.Referrals.Application.Rewards.ConfirmReferralRewardClawback;

public sealed record ConfirmReferralRewardClawbackCommand(
    Guid TenantId,
    Guid GrantId,
    Guid AttemptId,
    string ReversalReference,
    string IdempotencyKey,
    Guid ActorUserId
);
