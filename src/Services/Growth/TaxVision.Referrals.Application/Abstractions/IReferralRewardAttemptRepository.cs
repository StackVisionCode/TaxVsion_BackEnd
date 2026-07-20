using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralRewardAttemptRepository
{
    Task<ReferralRewardAttempt?> GetByIdAsync(
        Guid attemptId,
        Guid ownerTenantId,
        CancellationToken ct = default
    );

    Task AddAsync(ReferralRewardAttempt attempt, CancellationToken ct = default);
}
