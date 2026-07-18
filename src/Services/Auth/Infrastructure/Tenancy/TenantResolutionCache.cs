using BuildingBlocks.Caching;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Tenancy;

public sealed class TenantResolutionCache(ICacheService cache) : ITenantResolutionCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public Task<Guid?> TryGetAsync(string host, CancellationToken ct = default) => cache.GetAsync<Guid?>(Key(host), ct);

    public Task SetAsync(string host, Guid tenantId, CancellationToken ct = default) =>
        cache.SetAsync(Key(host), tenantId, Ttl, ct);

    public Task InvalidateAsync(string host, CancellationToken ct = default) => cache.RemoveAsync(Key(host), ct);

    private static string Key(string host) => $"tenant-resolution:host:{host}";
}
