using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

public sealed class ReferralRewardAttemptRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : IReferralRewardAttemptRepository
{
    public Task<ReferralRewardAttempt?> GetByIdAsync(
        Guid attemptId,
        Guid ownerTenantId,
        CancellationToken ct = default
    ) =>
        attemptId == Guid.Empty || !TenantRepositoryGuard.Matches(tenantContext, ownerTenantId)
            ? Task.FromResult<ReferralRewardAttempt?>(null)
            : dbContext.ReferralRewardAttempts.FirstOrDefaultAsync(
                attempt => attempt.Id == attemptId && attempt.TenantId == ownerTenantId,
                ct
            );

    public async Task AddAsync(ReferralRewardAttempt attempt, CancellationToken ct = default)
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, attempt.TenantId))
        {
            var elevatedRewardIsTracked =
                tenantContext.HasTenant
                && tenantContext.TenantId != Guid.Empty
                && dbContext
                    .ChangeTracker.Entries<ReferralRewardCase>()
                    .Any(entry => entry.Entity.Id == attempt.RewardCaseId && entry.Entity.TenantId == attempt.TenantId);

            if (!elevatedRewardIsTracked)
                TenantRepositoryGuard.EnsureMatches(tenantContext, attempt.TenantId);
        }

        await dbContext.ReferralRewardAttempts.AddAsync(attempt, ct);
    }
}
