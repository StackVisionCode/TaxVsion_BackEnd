using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Attributions;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

public sealed class ReferralAttributionRepository(
    GrowthDbContext dbContext,
    ITenantContext tenantContext
) : IReferralAttributionRepository
{
    public Task<ReferralAttribution?> GetByIdAsync(
        Guid attributionId,
        Guid ownerTenantId,
        CancellationToken ct = default
    ) =>
        attributionId == Guid.Empty
        || !TenantRepositoryGuard.Matches(tenantContext, ownerTenantId)
            ? Task.FromResult<ReferralAttribution?>(null)
            : dbContext.ReferralAttributions.FirstOrDefaultAsync(
                attribution =>
                    attribution.Id == attributionId
                    && attribution.TenantId == ownerTenantId,
                ct
            );

    public async Task AddAsync(
        ReferralAttribution attribution,
        CancellationToken ct = default
    )
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, attribution.TenantId);
        await dbContext.ReferralAttributions.AddAsync(attribution, ct);
    }
}
