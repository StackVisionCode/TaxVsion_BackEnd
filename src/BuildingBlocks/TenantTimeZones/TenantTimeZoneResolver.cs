using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.TenantTimeZones;

/// <summary>
/// Caché por proceso: 5 minutos para una zona proyectada y 1 minuto para el respaldo a UTC, así una oficina
/// recién creada no queda en UTC más de un minuto. Otras réplicas ven el cambio al vencer su caché.
/// </summary>
public sealed class TenantTimeZoneResolver(ITenantTimeZoneStore store, IMemoryCache cache, ILogger<TenantTimeZoneResolver> logger)
    : ITenantTimeZoneResolver
{
    private static readonly TimeSpan ProjectedTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FallbackTtl = TimeSpan.FromMinutes(1);

    public async Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(tenantId), out ResolvedTimeZone? cached) && cached is not null)
            return cached;

        var zoneId = await store.GetTimeZoneIdAsync(tenantId, ct);
        var resolved = ResolvedTimeZone.Resolve(zoneId);
        if (resolved.IsFallback)
            logger.LogWarning(
                "Tenant {TenantId} has no valid time zone projected ({TimeZoneId}); using {Fallback}.",
                tenantId,
                zoneId ?? "none",
                IanaTimeZone.UtcId
            );

        // Size=1: la IMemoryCache del servicio puede tener SizeLimit (Scribe).
        cache.Set(
            Key(tenantId),
            resolved,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = resolved.IsFallback ? FallbackTtl : ProjectedTtl, Size = 1 }
        );
        return resolved;
    }

    public void Forget(Guid tenantId) => cache.Remove(Key(tenantId));

    private static string Key(Guid tenantId) => $"tenant-time-zone:{tenantId:N}";
}
