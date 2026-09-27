using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Customer.Infrastructure.RateLimiting;

/// <summary>
/// Limpia las DOS cachés que viven sobre la misma fila de la proyección: el plan code del rate limit
/// y (A6/A5.6) los módulos habilitados del gate. El handler compartido de la proyección llama a este
/// puerto justo después de guardar, así que un cambio de plan se nota en el gate al instante en vez
/// de esperar el TTL — que solo queda como respaldo para el evento que se pierda.
/// </summary>
internal sealed class TenantPlanCodeCacheInvalidator(
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
