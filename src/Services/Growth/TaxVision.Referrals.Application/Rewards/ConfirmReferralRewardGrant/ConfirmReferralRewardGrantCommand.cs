namespace TaxVision.Referrals.Application.Rewards.ConfirmReferralRewardGrant;

public sealed record ConfirmReferralRewardGrantCommand(
    Guid TenantId,
    Guid GrantId,
    Guid AttemptId,
    string MaterializedBenefitReference,
    string IdempotencyKey,
    Guid ActorUserId
);
