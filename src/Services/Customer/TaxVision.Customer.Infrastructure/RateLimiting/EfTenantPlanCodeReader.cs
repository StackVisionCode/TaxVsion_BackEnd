using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.RateLimiting;

/// <summary>RateLimit Fase 6 — lee la proyección local mantenida por
/// TenantPlanCodeProjectionConsumer. Envuelto por CachedTenantPlanCodeReader (TTL 5 min) antes de
/// registrarse como ITenantPlanCodeReader.</summary>
internal sealed class EfTenantPlanCodeReader(CustomerDbContext db) : ITenantPlanCodeReader
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
