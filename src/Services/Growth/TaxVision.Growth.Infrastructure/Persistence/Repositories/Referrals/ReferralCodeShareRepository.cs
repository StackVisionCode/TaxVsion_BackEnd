using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

public sealed class ReferralCodeShareRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : IReferralCodeShareRepository
{
    public async Task AddAsync(ReferralCodeShare share, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, share.TenantId);
        await dbContext.ReferralCodeShares.AddAsync(share, ct);
    }
}
