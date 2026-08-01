using BuildingBlocks.Security;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Security;

public class ExpiringValueCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_CachesValue_UntilItExpires()
    {
        var cache = new ExpiringValueCache<string, string>(TimeSpan.FromSeconds(30));
        var factoryCalls = 0;

        Task<(string Value, DateTime ExpiresAtUtc)> Factory(CancellationToken ct)
        {
            factoryCalls++;
            return Task.FromResult(("token-1", DateTime.UtcNow.AddMinutes(5)));
        }

        var first = await cache.GetOrCreateAsync("key", Factory);
        var second = await cache.GetOrCreateAsync("key", Factory);

        Assert.Equal("token-1", first);
        Assert.Equal("token-1", second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrCreateAsync_RefreshesValue_WhenWithinRefreshBuffer()
    {
        var cache = new ExpiringValueCache<string, string>(TimeSpan.FromSeconds(30));
        var factoryCalls = 0;

        Task<(string Value, DateTime ExpiresAtUtc)> Factory(CancellationToken ct)
        {
            factoryCalls++;
            // Expires within the 30s refresh buffer — should be treated as a miss.
            return Task.FromResult(($"token-{factoryCalls}", DateTime.UtcNow.AddSeconds(10)));
        }

        var first = await cache.GetOrCreateAsync("key", Factory);
        var second = await cache.GetOrCreateAsync("key", Factory);

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task GetOrCreateAsync_RefreshesValue_AfterItExpires()
    {
        var cache = new ExpiringValueCache<string, string>(TimeSpan.Zero);
        var factoryCalls = 0;

        Task<(string Value, DateTime ExpiresAtUtc)> Factory(CancellationToken ct)
        {
            factoryCalls++;
            return Task.FromResult(($"token-{factoryCalls}", DateTime.UtcNow.AddMilliseconds(-1)));
        }

        var first = await cache.GetOrCreateAsync("key", Factory);
        var second = await cache.GetOrCreateAsync("key", Factory);

        Assert.Equal("token-1", first);
        Assert.Equal("token-2", second);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task GetOrCreateAsync_KeepsSeparateEntries_PerKey()
    {
        var cache = new ExpiringValueCache<string, string>(TimeSpan.FromSeconds(30));

        Task<(string Value, DateTime ExpiresAtUtc)> FactoryFor(string key, CancellationToken ct) =>
            Task.FromResult((key, DateTime.UtcNow.AddMinutes(5)));

        var a = await cache.GetOrCreateAsync("a", ct => FactoryFor("value-a", ct));
        var b = await cache.GetOrCreateAsync("b", ct => FactoryFor("value-b", ct));

        Assert.Equal("value-a", a);
        Assert.Equal("value-b", b);
    }
}
