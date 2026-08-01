using StackExchange.Redis;

namespace BuildingBlocks.Infrastructure.RateLimit;

/// <summary>
/// F26 — INCR + PEXPIRE-solo-en-el-primer-incremento como un único <c>EVAL</c> Lua, cerrando el
/// hueco no atómico de los 6 rate limiters que hacían <c>StringIncrementAsync</c> seguido de un
/// <c>KeyExpireAsync</c> separado (la clave queda sin TTL para siempre si el proceso muere entre
/// ambas llamadas).
/// </summary>
public sealed class RedisRateCounter(IConnectionMultiplexer redis) : IRateCounter
{
    private const string IncrementScript =
        @"
local count = redis.call('INCR', KEYS[1])
if count == 1 then
    redis.call('PEXPIRE', KEYS[1], ARGV[1])
end
return count";

    public async Task<long> IncrementAndGetAsync(RateCounterKey key, TimeSpan window, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var result = await db.ScriptEvaluateAsync(
            IncrementScript,
            new RedisKey[] { key.Value },
            new RedisValue[] { (long)window.TotalMilliseconds }
        );
        return (long)result;
    }
}
