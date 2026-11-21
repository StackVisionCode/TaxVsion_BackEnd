using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Infrastructure.Persistence;

namespace TaxVision.Mobile.Infrastructure.RateLimiting;

/// <summary>
/// Módulos habilitados del tenant para el gate. <c>null</c> = sin fila (no gatea); <c>[]</c> = sin módulos (deniega).
/// Se registra con <c>AddCachedTenantEntitlementModulesReader</c>.
/// </summary>
public sealed class EfTenantEntitlementModulesReader(MobileDbContext db) : ITenantEntitlementModulesReader
{
    public async Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Sin filtro global: la proyección del plan se lee con la oficina explícita.
        var projection = await db
            .TenantPlanCodeProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

        return projection?.EnabledModules;
    }
}
