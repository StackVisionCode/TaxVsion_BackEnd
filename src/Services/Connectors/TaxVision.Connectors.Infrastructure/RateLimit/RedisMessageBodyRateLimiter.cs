using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaxVision.Connectors.Application.Providers;

namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>Ventana fija de 1 minuto (INCR + EXPIRE) por (tenant, cuenta) — comparte presupuesto entre réplicas. Fail-fast: nunca espera, a diferencia de IProviderRateLimiter.</summary>
public sealed class RedisMessageBodyRateLimiter(
    IConnectionMultiplexer redis,
    IOptions<MessageBodyRateLimiterOptions> options
) : IMessageBodyRateLimiter
{
    public async Task<bool> TryAcquireAsync(Guid tenantId, Guid accountId, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var minuteBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var key = $"connectors:body-fetch:{tenantId:N}:{accountId:N}:window:{minuteBucket}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(65));

        return count <= options.Value.MaxRequestsPerMinute;
    }
}
