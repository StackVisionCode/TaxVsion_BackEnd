using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralRewardCaseRepository
{
    Task<ReferralRewardCase?> GetByIdAsync(Guid rewardCaseId, Guid ownerTenantId, CancellationToken ct = default);

    Task<ReferralRewardCase?> GetByGrantIdAsync(Guid grantId, Guid ownerTenantId, CancellationToken ct = default);

    /// <summary>
    /// Lookup elevado y exacto para compensaciones originadas por Payment. El caller M2M
    /// ya debe haber sido autorizado; nunca se usa para consultas interactivas.
    /// </summary>
    Task<ReferralRewardCase?> GetForCompensationAsync(Guid rewardCaseId, CancellationToken ct = default);

    Task AddAsync(ReferralRewardCase rewardCase, CancellationToken ct = default);
}
