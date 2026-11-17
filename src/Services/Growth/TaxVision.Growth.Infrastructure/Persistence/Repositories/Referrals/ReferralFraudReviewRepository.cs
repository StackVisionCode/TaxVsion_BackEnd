using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Fraud;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

public sealed class ReferralFraudReviewRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : IReferralFraudReviewRepository
{
    // Solo el PlatformAdmin revisa: lookup exacto por Id entre oficinas.
    public Task<ReferralFraudReview?> GetByIdAsync(Guid reviewId, CancellationToken ct = default) =>
        reviewId == Guid.Empty
            ? Task.FromResult<ReferralFraudReview?>(null)
            : dbContext
                .ReferralFraudReviews.IgnoreQueryFilters()
                .FirstOrDefaultAsync(review => review.Id == reviewId, ct);

    public async Task AddAsync(ReferralFraudReview review, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, review.TenantId);
        await dbContext.ReferralFraudReviews.AddAsync(review, ct);
    }
}
