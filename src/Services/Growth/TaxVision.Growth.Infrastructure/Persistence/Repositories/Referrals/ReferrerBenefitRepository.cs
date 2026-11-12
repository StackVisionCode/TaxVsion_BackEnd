using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Rewards;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

public sealed class ReferrerBenefitRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : IReferrerBenefitRepository
{
    public Task<ReferrerBenefit?> GetByRewardCaseAsync(
        Guid rewardCaseId,
        Guid ownerTenantId,
        CancellationToken ct = default
    ) =>
        rewardCaseId == Guid.Empty || !TenantRepositoryGuard.Matches(tenantContext, ownerTenantId)
            ? Task.FromResult<ReferrerBenefit?>(null)
            : dbContext
                .ReferrerBenefits.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    benefit => benefit.RewardCaseId == rewardCaseId && benefit.TenantId == ownerTenantId,
                    ct
                );

    public async Task AddAsync(ReferrerBenefit benefit, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, benefit.TenantId);
        await dbContext.ReferrerBenefits.AddAsync(benefit, ct);
    }
}
