using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Referrals.Application.Abstractions;
using TaxVision.Referrals.Domain.Attributions;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Referrals;

public sealed class OnboardingReferralRejectionRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : IOnboardingReferralRejectionRepository
{
    // El dueño de la anotación es el propio alta, que debe ser el tenant activo.
    public Task<bool> ExistsAsync(Guid onboardingId, CancellationToken ct = default) =>
        onboardingId == Guid.Empty || !TenantRepositoryGuard.Matches(tenantContext, onboardingId)
            ? Task.FromResult(false)
            : dbContext
                .OnboardingReferralRejections.IgnoreQueryFilters()
                .AnyAsync(
                    rejection => rejection.OnboardingId == onboardingId && rejection.TenantId == onboardingId,
                    ct
                );

    public async Task AddAsync(OnboardingReferralRejection rejection, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, rejection.TenantId);
        await dbContext.OnboardingReferralRejections.AddAsync(rejection, ct);
    }
}
