using BuildingBlocks.Infrastructure.RateLimiting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.RateLimit;

/// <summary>
/// Ventana fija por minuto — clave <c>postmaster:ratelimit:{provider}:{tenant}:{stream}:{yyyyMMddHHmm}</c>.
/// El <c>stream</c> forma parte de la clave (no solo un dato adicional) para que Bulk y Transactional
/// nunca compartan balde — ver doc-comment de <see cref="IEmailProviderRateLimiter"/>. El incremento en
/// sí es atómico vía <see cref="IRateCounter"/>; el TTL de la respuesta 429 (<c>KeyTimeToLiveAsync</c>)
/// sigue leyendo directo de <see cref="IConnectionMultiplexer"/> porque no forma parte del contrato de
/// incremento. Fail-open ante caída de Redis (invariante §3.3 del plan de rate limiting): esta clase es
/// F26-era y no heredaba la garantía que sí tiene <c>TieredRateLimitEvaluator</c> desde Fase 3.
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
        EmailStream stream,
        int limitPerMinute,
        CancellationToken ct = default
    )
    {
        var key = BuildKey(providerCode, tenantId, stream);
        long count;
        try
        {
            count = await rateCounter.IncrementAndGetAsync(key, TimeSpan.FromMinutes(1), ct);
        }
        catch (RedisException ex)
        {
            logger.LogWarning(
                ex,
                "Redis no disponible para rate limit de provider {ProviderCode}/tenant {TenantId}/stream {Stream} — fail-open",
                providerCode,
                tenantId,
                stream
            );
            return new RateLimitDecision(true, null);
        }

        if (count <= limitPerMinute)
            return new RateLimitDecision(true, null);

        var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(key.Value);
        PostmasterMetrics.RateLimitHits.Add(
            1,
            new KeyValuePair<string, object?>("provider", providerCode),
            new KeyValuePair<string, object?>("tenant", tenantId.ToString()),
            new KeyValuePair<string, object?>("stream", stream.ToString())
        );
        return new RateLimitDecision(false, ttl ?? TimeSpan.FromSeconds(60));
    }

    private static RateCounterKey BuildKey(string providerCode, Guid tenantId, EmailStream stream) =>
        RateCounterKey.From($"postmaster:ratelimit:{providerCode}:{tenantId}:{stream}:{DateTime.UtcNow:yyyyMMddHHmm}");
}
