using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
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

    public async Task<Guid?> FindReferralCodeIdAsync(Guid shareId, CancellationToken ct = default) =>
        shareId == Guid.Empty
            ? null
            : await dbContext
                .ReferralCodeShares.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(share => share.Id == shareId)
                .Select(share => (Guid?)share.ReferralCodeId)
                .FirstOrDefaultAsync(ct);
}
