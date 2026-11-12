using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralRewardAttemptRepository
{
    Task<ReferralRewardAttempt?> GetByIdAsync(Guid attemptId, Guid ownerTenantId, CancellationToken ct = default);

    /// <summary>El intento de acreditación (Grant) aún abierto de ese caso, o null. Permite reanudar tras un fallo del Wallet.</summary>
    Task<ReferralRewardAttempt?> GetOpenGrantAttemptAsync(
        Guid rewardCaseId,
        Guid ownerTenantId,
        CancellationToken ct = default
    );

    Task AddAsync(ReferralRewardAttempt attempt, CancellationToken ct = default);
}
