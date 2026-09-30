using BuildingBlocks.Caching;
using BuildingBlocks.RateLimiting;

namespace BuildingBlocks.Infrastructure.RateLimiting;

/// <summary>
/// A6/A5.6 — decorador de caché sobre el lector de módulos del gate. Mismo patrón que
/// <see cref="CachedTenantPlanCodeReader"/>, que cachea la otra lectura de la misma fila.
///
/// Existe porque el gate consulta los módulos en CADA request a un endpoint con permiso gateado, y
/// con el enforce encendido eso es una query por request en todos los servicios mapeados. La fila
/// cambia solo cuando llega un <c>TenantEntitlementsChangedIntegrationEvent</c>, así que casi todas
/// esas queries devuelven lo mismo.
///
/// <para><b>TTL de 1 minuto, no de 5</b> como el plan code. La invalidación por evento es el camino
/// normal (la hace el mismo invalidador que el handler compartido de la proyección ya llama al
/// guardar); el TTL solo es el respaldo para el evento que se perdió. Y el precio de una entrada
/// vieja acá no es un límite de rate mal calculado: es un 403 en algo que el tenant sí paga —o, al
/// revés, acceso a algo que ya no— así que la ventana se deja corta a propósito.</para>
///
/// <para><b>El <c>null</c> no se cachea y la lista vacía sí.</b> No es un detalle de rendimiento,
/// es la semántica del gate: <c>null</c> = "todavía no llegó la proyección" (el gate no deniega), y
/// <c>[]</c> = "sé que no tiene ningún módulo" (el gate deniega — un tenant vencido). Cachear el
/// <c>null</c> alargaría la ventana de fail-open de un tenant recién creado; no cachear el <c>[]</c>
/// dejaría sin efecto la caché justo para los tenants vencidos, que son los que más la necesitan.
/// El envoltorio <see cref="Entry"/> mantiene esa distinción viva: una entrada cacheada es un objeto
/// no nulo aunque su lista esté vacía, así que "no hay entrada" nunca se confunde con "no hay
/// módulos".</para>
/// </summary>
public sealed class CachedTenantEntitlementModulesReader(ICacheService cache, ITenantEntitlementModulesReader inner)
    : ITenantEntitlementModulesReader
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    /// <summary>Envoltorio para que una lista vacía cacheada siga siendo una entrada presente.</summary>
    private sealed record Entry(string[] Modules);

    public async Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var key = CacheKey(tenantId);
        var cached = await cache.GetAsync<Entry>(key, ct).ConfigureAwait(false);
        if (cached is not null)
            return cached.Modules;

        var modules = await inner.GetEnabledModulesAsync(tenantId, ct).ConfigureAwait(false);
        if (modules is not null)
            await cache.SetAsync(key, new Entry([.. modules]), Ttl, ct).ConfigureAwait(false);

        return modules;
    }

    public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default) =>
        cache.RemoveAsync(CacheKey(tenantId), ct);

    private static string CacheKey(Guid tenantId) => $"authz:modules:{tenantId:N}";
}
