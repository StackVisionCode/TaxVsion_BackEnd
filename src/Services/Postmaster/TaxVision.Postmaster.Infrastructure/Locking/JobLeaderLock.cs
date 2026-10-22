using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace TaxVision.Postmaster.Infrastructure.Locking;

/// <summary>
/// Con varias réplicas, solo una corre cada vuelta de un job. Redis SET NX con TTL; se suelta solo si
/// sigue siendo nuestro. Sin Redis (dev de un nodo) siempre se obtiene.
/// </summary>
public sealed class JobLeaderLock(IServiceProvider services)
{
    private const string ReleaseScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

    /// <summary>Null = otra réplica tiene el turno.</summary>
    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl)
    {
        var redis = services.GetService<IConnectionMultiplexer>();
        if (redis is null)
            return new Lease(null, key, string.Empty);

        var token = Guid.NewGuid().ToString("N");
        var acquired = await redis.GetDatabase().StringSetAsync(key, token, ttl, When.NotExists);
        return acquired ? new Lease(redis, key, token) : null;
    }

    private sealed class Lease(IConnectionMultiplexer? redis, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (redis is not null)
                await redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, [new RedisKey(key)], [token]);
        }
    }
}
