using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Infrastructure.Persistence;

namespace TaxVision.Mobile.Infrastructure.RateLimiting;

/// <summary>Plan del tenant desde la proyección local (lo cachea <c>CachedTenantPlanCodeReader</c>).</summary>
internal sealed class EfTenantPlanCodeReader(MobileDbContext db) : ITenantPlanCodeReader
{
    public async Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Sin filtro global: la proyección del plan se lee con la oficina explícita.
        var projection = await db
            .TenantPlanCodeProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

        return projection?.PlanCode;
    }
}
