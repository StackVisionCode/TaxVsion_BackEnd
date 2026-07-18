using StackExchange.Redis;
using TaxVision.Postmaster.Application.RateLimit;

namespace TaxVision.Postmaster.Infrastructure.RateLimit;

/// <summary>
/// Ventana fija por minuto vía INCR+EXPIRE — clave <c>postmaster:ratelimit:{provider}:{tenant}:{yyyyMMddHHmm}</c>.
/// INCR es atómico en Redis; el primer request de la ventana fija el TTL a 60s.
/// </summary>
public sealed class RedisEmailProviderRateLimiter(IConnectionMultiplexer redis) : IEmailProviderRateLimiter
{
    public async Task<RateLimitDecision> AcquireAsync(
        string providerCode,
        Guid tenantId,
        int limitPerMinute,
        CancellationToken ct = default
    )
    {
        var db = redis.GetDatabase();
        var key = BuildKey(providerCode, tenantId);

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, TimeSpan.FromMinutes(1));

        if (count <= limitPerMinute)
            return new RateLimitDecision(true, null);

        var ttl = await db.KeyTimeToLiveAsync(key);
        PostmasterMetrics.RateLimitHits.Add(
            1,
            new KeyValuePair<string, object?>("provider", providerCode),
            new KeyValuePair<string, object?>("tenant", tenantId.ToString())
        );
        return new RateLimitDecision(false, ttl ?? TimeSpan.FromSeconds(60));
    }

    private static string BuildKey(string providerCode, Guid tenantId) =>
        $"postmaster:ratelimit:{providerCode}:{tenantId}:{DateTime.UtcNow:yyyyMMddHHmm}";
}
