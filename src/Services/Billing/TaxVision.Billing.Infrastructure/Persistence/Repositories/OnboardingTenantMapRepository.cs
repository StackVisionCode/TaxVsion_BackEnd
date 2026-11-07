using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

public sealed class OnboardingTenantMapRepository(BillingDbContext dbContext) : IOnboardingTenantMapRepository
{
    public async Task UpsertAsync(Guid onboardingId, Guid tenantId, DateTime nowUtc, CancellationToken ct = default)
    {
        var map = await dbContext.OnboardingTenantMaps.FirstOrDefaultAsync(m => m.OnboardingId == onboardingId, ct);
        if (map is null)
            await dbContext.OnboardingTenantMaps.AddAsync(
                OnboardingTenantMap.Create(onboardingId, tenantId, nowUtc),
                ct
            );
        else
            map.PointTo(tenantId);
    }

    public Task<Guid?> FindTenantIdAsync(Guid onboardingId, CancellationToken ct = default) =>
        dbContext
            .OnboardingTenantMaps.Where(m => m.OnboardingId == onboardingId)
            .Select(m => (Guid?)m.TenantId)
            .FirstOrDefaultAsync(ct);
}
