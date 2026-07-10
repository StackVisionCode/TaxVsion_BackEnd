using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Locking;

/// <summary>
/// Distributed lock con Redis SET NX PX. Al disposeal el handle, ejecuta un script
/// Lua que borra la clave solo si el valor coincide con el token — evita liberar el
/// lock de otro nodo si el TTL expira antes.
/// </summary>
public sealed class RedisDistributedLock(IConnectionMultiplexer redis, ILogger<RedisDistributedLock> logger)
    : IDistributedLock
{
    private const string ReleaseScript =
        @"
if redis.call('GET', KEYS[1]) == ARGV[1] then
    return redis.call('DEL', KEYS[1])
else
    return 0
end";

    public async Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var db = redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var acquired = await db.StringSetAsync(key, token, ttl, when: When.NotExists);
        if (!acquired)
            return new UnacquiredHandle(key);
        return new RedisHandle(db, key, token, logger);
    }

    private sealed class RedisHandle(IDatabase db, string key, string token, ILogger logger) : ILockHandle
    {
        public bool IsAcquired => true;
        public string Key => key;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.ScriptEvaluateAsync(ReleaseScript, new RedisKey[] { key }, new RedisValue[] { token });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis lock release failed for key {Key}; TTL expiration will clean it up.", key);
            }
        }
    }

    private sealed class UnacquiredHandle(string key) : ILockHandle
    {
        public bool IsAcquired => false;
        public string Key { get; } = key;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
