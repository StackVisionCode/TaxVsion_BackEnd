using BuildingBlocks.Infrastructure.RateLimit;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TaxVision.Postmaster.Application.RateLimit;

namespace TaxVision.Postmaster.Infrastructure.RateLimit;

/// <summary>
/// Ventana fija por minuto — clave <c>postmaster:ratelimit:{provider}:{tenant}:{yyyyMMddHHmm}</c>.
/// El incremento en sí es atómico vía <see cref="IRateCounter"/>; el TTL de la respuesta 429
/// (<c>KeyTimeToLiveAsync</c>) sigue leyendo directo de <see cref="IConnectionMultiplexer"/> porque
/// no forma parte del contrato de incremento. Fail-open ante caída de Redis (invariante §3.3 del
/// plan de rate limiting): esta clase es F26-era y no heredaba la garantía que sí tiene
/// <c>TieredRateLimitEvaluator</c> desde Fase 3.
/// </summary>
public sealed class RedisEmailProviderRateLimiter(
    IConnectionMultiplexer redis,
    IRateCounter rateCounter,
    ILogger<RedisEmailProviderRateLimiter> logger
) : IEmailProviderRateLimiter
{
    public async Task<RateLimitDecision> AcquireAsync(
        string providerCode,
        Guid tenantId,
        int limitPerMinute,
        CancellationToken ct = default
    )
    {
        var key = BuildKey(providerCode, tenantId);
        long count;
        try
        {
            count = await rateCounter.IncrementAndGetAsync(key, TimeSpan.FromMinutes(1), ct);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(
                ex,
                "Redis no disponible para rate limit de provider {ProviderCode}/tenant {TenantId} — fail-open",
                providerCode,
                tenantId
            );
            return new RateLimitDecision(true, null);
        }

        if (count <= limitPerMinute)
            return new RateLimitDecision(true, null);

        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(key.Value);
        PostmasterMetrics.RateLimitHits.Add(
            1,
            new KeyValuePair<string, object?>("provider", providerCode),
            new KeyValuePair<string, object?>("tenant", tenantId.ToString())
        );
        return new RateLimitDecision(false, ttl ?? TimeSpan.FromSeconds(60));
    }

    private static RateCounterKey BuildKey(string providerCode, Guid tenantId) =>
        RateCounterKey.From($"postmaster:ratelimit:{providerCode}:{tenantId}:{DateTime.UtcNow:yyyyMMddHHmm}");
}
