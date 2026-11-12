using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferrerBenefitRepository
{
    /// <summary>Beneficio registrado para ese caso de premio (único por caso), o null. El tenant activo debe ser el dueño.</summary>
    Task<ReferrerBenefit?> GetByRewardCaseAsync(Guid rewardCaseId, Guid ownerTenantId, CancellationToken ct = default);

    Task AddAsync(ReferrerBenefit benefit, CancellationToken ct = default);
}
