using BuildingBlocks.Caching;
using TaxVision.Auth.Infrastructure.Tenancy;

namespace TaxVision.Auth.Tests.Infrastructure;

/// <summary>Fase A6 — v2 doc §26.2 item 8: la cache key incluye el Host, así que dos hosts nunca se mezclan.</summary>
public sealed class TenantResolutionCacheTests
{
    private sealed class InMemoryCacheService : ICacheService
    {
        private readonly Dictionary<string, object> _store = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            _store[key] = value!;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        )
        {
            if (_store.TryGetValue(key, out var cached))
                return (T)cached;

            var created = await factory(ct);
            _store[key] = created!;
            return created;
        }
    }

    [Fact]
    public async Task Two_hosts_never_share_a_cache_entry()
    {
        var cache = new TenantResolutionCache(new InMemoryCacheService());
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await cache.SetAsync("oficina1.taxprocore.com", tenantA);
        await cache.SetAsync("oficina2.taxprocore.com", tenantB);

        Assert.Equal(tenantA, await cache.TryGetAsync("oficina1.taxprocore.com"));
        Assert.Equal(tenantB, await cache.TryGetAsync("oficina2.taxprocore.com"));
    }

    [Fact]
    public async Task Unseen_host_never_returns_another_hosts_cached_tenant()
    {
        var cache = new TenantResolutionCache(new InMemoryCacheService());
        await cache.SetAsync("oficina1.taxprocore.com", Guid.NewGuid());

        Assert.Null(await cache.TryGetAsync("oficina-nunca-vista.taxprocore.com"));
    }

    [Fact]
    public async Task Invalidating_one_host_does_not_affect_another()
    {
        var cache = new TenantResolutionCache(new InMemoryCacheService());
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await cache.SetAsync("oficina1.taxprocore.com", tenantA);
        await cache.SetAsync("oficina2.taxprocore.com", tenantB);

        await cache.InvalidateAsync("oficina1.taxprocore.com");

        Assert.Null(await cache.TryGetAsync("oficina1.taxprocore.com"));
        Assert.Equal(tenantB, await cache.TryGetAsync("oficina2.taxprocore.com"));
    }
}
