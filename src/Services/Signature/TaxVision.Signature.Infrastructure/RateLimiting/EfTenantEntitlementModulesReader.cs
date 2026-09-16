using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Infrastructure.RateLimiting;

/// <summary>
/// Gate de módulo Fase 1 (piloto Signature) — lee los módulos habilitados desde la misma proyección
/// local que mantiene <c>TenantPlanCodeProjectionConsumer</c>. Lectura fresca <c>AsNoTracking</c>
/// (una fila por tenant, índice único en TenantId): en modo log-only se prioriza el dato fresco
/// sobre cachearlo, para no producir falsos "deny" por caché stale — la caché se añade en Fase 2
/// (enforce), con invalidación por evento.
/// </summary>
internal sealed class EfTenantEntitlementModulesReader(SignatureDbContext db) : ITenantEntitlementModulesReader
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
