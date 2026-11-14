using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;

namespace TaxVision.FinancialInsights.Infrastructure.RateLimiting;

/// <summary>
/// Limpia las dos cachés que viven sobre la misma fila de la proyección: el plan del rate limit y los módulos del
/// gate. Se llama justo después de guardar un cambio de plan, así el gate lo nota al instante y no tras el TTL.
/// </summary>
public sealed class TenantPlanCodeCacheInvalidator(
    CachedTenantPlanCodeReader planCode,
    CachedTenantEntitlementModulesReader modules
) : ITenantPlanCodeCacheInvalidator
{
    public async Task InvalidateAsync(Guid tenantId, CancellationToken ct = default)
    {
        await planCode.InvalidateAsync(tenantId, ct);
        await modules.InvalidateAsync(tenantId, ct);
    }
}
