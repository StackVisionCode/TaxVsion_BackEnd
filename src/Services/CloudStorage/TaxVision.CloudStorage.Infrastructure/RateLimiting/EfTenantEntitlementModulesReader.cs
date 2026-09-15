using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Infrastructure.RateLimiting;

/// <summary>
/// Gate de módulo Fase 1 — lee los módulos habilitados desde la misma proyección local. Lectura
/// fresca AsNoTracking (log-only: sin caché para no producir falsos "deny"; caché en Fase 2 enforce).
/// </summary>
internal sealed class EfTenantEntitlementModulesReader(CloudStorageDbContext db) : ITenantEntitlementModulesReader
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
