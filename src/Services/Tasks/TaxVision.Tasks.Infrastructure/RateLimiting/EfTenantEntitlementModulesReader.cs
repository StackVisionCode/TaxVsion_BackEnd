using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.RateLimiting;

/// <summary>
/// Gate de módulo (log-only) — lee los módulos habilitados de la proyección local. AsNoTracking fresco
/// (sin caché para no producir falsos "deny"; la caché entra cuando el gate pase a enforce).
/// </summary>
internal sealed class EfTenantEntitlementModulesReader(TasksDbContext db) : ITenantEntitlementModulesReader
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
