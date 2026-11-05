using StackExchange.Redis;

namespace TaxVision.Wallet.Infrastructure.Scheduling;

/// <summary>Candado en Redis (SET NX con TTL); solo lo suelta quien lo tomó. Copia del de PaymentApp.</summary>
public sealed class RedisDistributedLockFactory(IConnectionMultiplexer redis) : IDistributedLockFactory
{
    private const string ReleaseIfOwnerScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var redisKey = new RedisKey($"wallet:lock:{key}");
        var token = Guid.NewGuid().ToString("N");

        var acquired = await db.StringSetAsync(redisKey, token, ttl, When.NotExists);
        return acquired ? new RedisLockHandle(db, redisKey, token) : null;
    }

    private sealed class RedisLockHandle(IDatabase db, RedisKey key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await db.ScriptEvaluateAsync(ReleaseIfOwnerScript, [key], [token]);
    }
}
