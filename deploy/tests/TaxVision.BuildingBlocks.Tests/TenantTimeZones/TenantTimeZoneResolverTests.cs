using BuildingBlocks.TenantTimeZones;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.TenantTimeZones;

public sealed class TenantTimeZoneResolverTests
{
    // En vivo: Scribe configura su caché con SizeLimit y todo correo al cliente sin zona explícita fallaba al renderizar.
    [Fact]
    public async Task Works_with_a_shared_cache_that_has_a_size_limit()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var resolver = new TenantTimeZoneResolver(
            new FixedStore("America/Los_Angeles"),
            cache,
            NullLogger<TenantTimeZoneResolver>.Instance
        );
        var tenant = Guid.NewGuid();

        var first = await resolver.ResolveAsync(tenant);
        var cached = await resolver.ResolveAsync(tenant);

        Assert.Equal("America/Los_Angeles", first.Id);
        Assert.Same(first, cached);
    }

    private sealed class FixedStore(string zone) : ITenantTimeZoneStore
    {
        public Task<TenantTimeZoneProjection?> FindAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantTimeZoneProjection?>(null);

        public Task<string?> GetTimeZoneIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>(zone);

        public Task AddAsync(TenantTimeZoneProjection projection, CancellationToken ct = default) => Task.CompletedTask;
    }
}
