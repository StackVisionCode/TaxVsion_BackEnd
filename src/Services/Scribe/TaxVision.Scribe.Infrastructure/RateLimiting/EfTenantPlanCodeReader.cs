using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Infrastructure.Persistence;

namespace TaxVision.Scribe.Infrastructure.RateLimiting;

/// <summary>RateLimit Fase 2 — lee la proyección local mantenida por TenantPlanCodeProjectionConsumer.</summary>
internal sealed class EfTenantPlanCodeReader(ScribeDbContext db) : ITenantPlanCodeReader
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
