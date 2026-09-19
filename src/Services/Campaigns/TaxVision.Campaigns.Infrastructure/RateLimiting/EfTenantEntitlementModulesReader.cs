using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Infrastructure.Persistence;

namespace TaxVision.Campaigns.Infrastructure.RateLimiting;

/// <summary>
/// Gate de módulo — lee los módulos habilitados de la proyección local. AsNoTracking fresco (sin
/// caché para no producir falsos "deny").
/// </summary>
internal sealed class EfTenantEntitlementModulesReader(CampaignsDbContext db) : ITenantEntitlementModulesReader
{
    public async Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var projection = await db
            .TenantPlanCodeProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

        return projection?.EnabledModules;
    }
}
