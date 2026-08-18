using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Infrastructure.RateLimiting;

/// <summary>RateLimit Fase 2 — lee la proyección local mantenida por TenantPlanCodeProjectionConsumer.</summary>
internal sealed class EfTenantPlanCodeReader(PostmasterDbContext db) : ITenantPlanCodeReader
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
