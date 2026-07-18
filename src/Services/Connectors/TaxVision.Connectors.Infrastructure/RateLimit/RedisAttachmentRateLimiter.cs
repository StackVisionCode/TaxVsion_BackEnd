using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaxVision.Connectors.Application.Providers;

namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>Ventana fija de 1 minuto por tenant (no por cuenta, a diferencia de RedisMessageBodyRateLimiter) — comparte presupuesto entre réplicas. Fail-fast.</summary>
public sealed class RedisAttachmentRateLimiter(
    IConnectionMultiplexer redis,
    IOptions<AttachmentRateLimiterOptions> options
) : IAttachmentRateLimiter
{
    public async Task<bool> TryAcquireAsync(Guid tenantId, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var minuteBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var key = $"connectors:attachment-fetch:{tenantId:N}:window:{minuteBucket}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(65));

        return count <= options.Value.MaxRequestsPerMinute;
    }
}
