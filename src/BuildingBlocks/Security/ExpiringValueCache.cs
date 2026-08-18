using System.Collections.Concurrent;

namespace BuildingBlocks.Security;

/// <summary>
/// F25 — cache genérica de valores con expiración y margen de refresco, extraída de los ~10
/// copias casi idénticas de "<c>ConcurrentDictionary</c> + expiración + margen" repartidas por
/// servicio (cache de tokens M2M en Auth/Tenant/Scribe/Signature/Correspondence/Customer/
/// Notification/Postmaster/Subscription). Una carrera entre dos llamadas concurrentes que
/// encuentran la cache vacía puede invocar <paramref name="factory"/> dos veces — aceptable
/// por diseño: el objetivo es eliminar el costo por-llamada en el caso común, no garantizar
/// exactamente-una-adquisición bajo concurrencia.
/// </summary>
public sealed class ExpiringValueCache<TKey, TValue>(TimeSpan refreshBuffer)
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, CacheEntry> _cache = new();

    public async Task<TValue> GetOrCreateAsync(
        TKey key,
        Func<CancellationToken, Task<(TValue Value, DateTime ExpiresAtUtc)>> factory,
        CancellationToken ct = default
    )
    {
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow + refreshBuffer)
            return cached.Value;

        var (value, expiresAtUtc) = await factory(ct);
        _cache[key] = new CacheEntry(value, expiresAtUtc);
        return value;
    }

    private readonly record struct CacheEntry(TValue Value, DateTime ExpiresAtUtc);
}
