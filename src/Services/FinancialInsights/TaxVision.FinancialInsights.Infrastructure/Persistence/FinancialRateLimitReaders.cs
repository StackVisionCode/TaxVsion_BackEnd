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
