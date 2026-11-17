namespace TaxVision.Referrals.Application.Rewards.BeginReferralRewardGrant;

/// <summary>Reservado para premios de suscripción del referidor, hoy bloqueados (el crédito de Wallet usa su propio flujo).</summary>
public sealed record BeginReferralRewardGrantCommand(
    Guid TenantId,
    Guid RewardCaseId,
    string IdempotencyKey,
    Guid ActorUserId
);
