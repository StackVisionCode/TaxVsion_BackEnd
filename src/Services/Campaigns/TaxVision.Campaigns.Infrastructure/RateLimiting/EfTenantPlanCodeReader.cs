using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Infrastructure.Persistence;

namespace TaxVision.Campaigns.Infrastructure.RateLimiting;

/// <summary>Lee la proyección local mantenida por TenantPlanCodeProjectionConsumer.</summary>
internal sealed class EfTenantPlanCodeReader(CampaignsDbContext db) : ITenantPlanCodeReader
{
    public async Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default)
    {
        var projection = await db
            .TenantPlanCodeProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

        return projection?.PlanCode;
    }
}
