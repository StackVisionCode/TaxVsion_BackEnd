namespace TaxVision.Referrals.Application.Rewards.RequestReferralRewardClawback;

/// <summary>Reservado para premios de suscripción del referidor, hoy bloqueados (el crédito de Wallet usa su propio flujo).</summary>
public sealed record RequestReferralRewardClawbackCommand(
    Guid TenantId,
    Guid RewardCaseId,
    Guid SourceEventId,
    string Reason,
    string IdempotencyKey,
    Guid ActorUserId
);
