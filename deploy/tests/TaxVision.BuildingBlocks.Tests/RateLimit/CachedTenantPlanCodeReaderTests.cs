using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

public sealed class CachedTenantPlanCodeReaderTests
{
    private readonly Guid tenantId = Guid.NewGuid();

    [Fact]
    public async Task First_call_reads_through_to_the_inner_reader_and_caches_it()
    {
        var cache = new FakeCacheService();
        var inner = new CountingTenantPlanCodeReader("pro");
        var reader = new CachedTenantPlanCodeReader(cache, inner);

        var first = await reader.GetPlanCodeAsync(tenantId);
        var second = await reader.GetPlanCodeAsync(tenantId);

        Assert.Equal("pro", first);
        Assert.Equal("pro", second);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Unknown_tenant_is_never_cached_so_it_keeps_reading_through()
    {
        var cache = new FakeCacheService();
        var inner = new CountingTenantPlanCodeReader(planCode: null);
        var reader = new CachedTenantPlanCodeReader(cache, inner);

        await reader.GetPlanCodeAsync(tenantId);
        await reader.GetPlanCodeAsync(tenantId);

        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task InvalidateAsync_forces_the_next_call_to_read_through_again()
    {
        var cache = new FakeCacheService();
        var inner = new CountingTenantPlanCodeReader("pro");
        var reader = new CachedTenantPlanCodeReader(cache, inner);

        await reader.GetPlanCodeAsync(tenantId);
        await reader.InvalidateAsync(tenantId);
        await reader.GetPlanCodeAsync(tenantId);

        Assert.Equal(2, inner.CallCount);
    }

    private sealed class CountingTenantPlanCodeReader(string? planCode) : ITenantPlanCodeReader
    {
        public int CallCount { get; private set; }

        public Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(planCode);
        }
    }

    private sealed class FakeCacheService : ICacheService
    {
        private readonly Dictionary<string, object?> store = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(store.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            store.Remove(key);
            return Task.CompletedTask;
        }

        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        )
        {
            var cached = await GetAsync<T>(key, ct);
            if (cached is not null)
                return cached;
            var value = await factory(ct);
            await SetAsync(key, value, ttl, ct);
            return value;
        }
    }
}
