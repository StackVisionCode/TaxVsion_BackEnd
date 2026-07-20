namespace TaxVision.Referrals.Application.Codes.IssueTenantReferralCode;

public sealed record IssueTenantReferralCodeCommand(
    Guid TenantId,
    Guid ProgramId,
    DateTime ExpiresAtUtc,
    Guid ActorUserId,
    string IdempotencyKey
);
