using BuildingBlocks.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Infrastructure.Security;

namespace TaxVision.Signature.Tests;

public class CachedJtiDenylistTests
{
    [Fact]
    public async Task Returns_false_when_token_was_never_revoked()
    {
        var denylist = new CachedJtiDenylist(new FakeCache(), NullLogger<CachedJtiDenylist>.Instance);

        Assert.False(await denylist.IsRevokedAsync("jti-1"));
    }

    [Fact]
    public async Task Returns_true_after_the_token_is_revoked()
    {
        var denylist = new CachedJtiDenylist(new FakeCache(), NullLogger<CachedJtiDenylist>.Instance);

        await denylist.RevokeAsync("jti-1", DateTime.UtcNow.AddMinutes(10));

        Assert.True(await denylist.IsRevokedAsync("jti-1"));
    }

    [Fact]
    public async Task Ignores_a_revoke_that_is_already_expired()
    {
        var denylist = new CachedJtiDenylist(new FakeCache(), NullLogger<CachedJtiDenylist>.Instance);

        await denylist.RevokeAsync("jti-1", DateTime.UtcNow.AddMinutes(-1));

        Assert.False(await denylist.IsRevokedAsync("jti-1"));
    }

    [Fact]
    public async Task Fails_open_when_the_store_is_unavailable()
    {
        var denylist = new CachedJtiDenylist(new FakeCache { Throw = true }, NullLogger<CachedJtiDenylist>.Instance);

        Assert.False(await denylist.IsRevokedAsync("jti-1"));
    }

    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object?> _store = [];
        public bool Throw { get; init; }

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            if (Throw)
                throw new InvalidOperationException("cache down");
            return Task.FromResult(_store.TryGetValue(key, out var v) ? (T?)v : default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        {
            if (Throw)
                throw new InvalidOperationException("cache down");
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
