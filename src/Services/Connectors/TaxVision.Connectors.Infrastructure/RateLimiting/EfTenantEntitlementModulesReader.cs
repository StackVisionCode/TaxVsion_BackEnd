using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Infrastructure.Persistence;

namespace TaxVision.Connectors.Infrastructure.RateLimiting;

/// <summary>
/// Gate de módulo Fase 1 — lee los módulos habilitados de la proyección local. AsNoTracking fresco
/// (log-only: sin caché para no producir falsos "deny"; caché en Fase 2 enforce).
/// </summary>
internal sealed class EfTenantEntitlementModulesReader(ConnectorsDbContext db) : ITenantEntitlementModulesReader
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
