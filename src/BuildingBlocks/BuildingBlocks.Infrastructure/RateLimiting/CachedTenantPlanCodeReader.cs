using BuildingBlocks.Caching;
using BuildingBlocks.RateLimiting;

namespace BuildingBlocks.Infrastructure.RateLimiting;

/// <summary>
/// Decorador de caché (TTL 5 min, invariante §8 Fase 2 — "resuelve el tier del tenant desde
/// caché Redis") sobre un <see cref="ITenantPlanCodeReader"/> interno cualquiera (M2M+HTTP,
/// proyección EF, lo que decida Fase 6 para cada servicio). Expone <see cref="InvalidateAsync"/>
/// para que el consumer de <c>TenantEntitlementsChangedIntegrationEvent</c> de cada servicio
/// (Fase 6) invalide la entrada al vuelo en vez de esperar el TTL.
///
/// <para>
/// Solo cachea resultados positivos — un tenant desconocido (null) no se cachea, así que un
/// tenant recién creado empieza a resolver correctamente en la próxima llamada sin esperar el
/// TTL, sin necesitar tampoco invalidación explícita en el alta.
/// </para>
/// </summary>
public sealed class CachedTenantPlanCodeReader(ICacheService cache, ITenantPlanCodeReader inner) : ITenantPlanCodeReader
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default)
    {
        var key = CacheKey(tenantId);
        var cached = await cache.GetAsync<string>(key, ct).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        var planCode = await inner.GetPlanCodeAsync(tenantId, ct).ConfigureAwait(false);
        if (planCode is not null)
            await cache.SetAsync(key, planCode, Ttl, ct).ConfigureAwait(false);

        return planCode;
    }

    public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default) =>
        cache.RemoveAsync(CacheKey(tenantId), ct);

    private static string CacheKey(Guid tenantId) => $"ratelimit:plancode:{tenantId:N}";
}
