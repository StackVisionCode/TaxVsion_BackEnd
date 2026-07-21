using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Qualifications;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

public sealed class ReferralQualificationRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : IReferralQualificationRepository
{
    public async Task AddAsync(ReferralQualification qualification, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, qualification.TenantId);
        await dbContext.ReferralQualifications.AddAsync(qualification, ct);
    }
}
