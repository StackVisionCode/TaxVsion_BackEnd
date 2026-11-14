using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace TaxVision.FinancialInsights.Infrastructure.Persistence;

public sealed class FinancialTenantPlanCodeReader(FinancialInsightsDbContext db) : ITenantPlanCodeReader
{
    public async Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantEntitlementsProjections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.PlanCode)
            .SingleOrDefaultAsync(ct);
}

public sealed class FinancialEntitlementsCacheInvalidator(
    CachedTenantPlanCodeReader planCode,
    CachedTenantEntitlementModulesReader modules)
{
    public async Task InvalidateAsync(Guid tenantId, CancellationToken ct = default)
    {
        await planCode.InvalidateAsync(tenantId, ct);
        await modules.InvalidateAsync(tenantId, ct);
    }
}
