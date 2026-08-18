using BuildingBlocks.Infrastructure.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Providers;

namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>Ventana fija de 1 minuto por tenant (no por cuenta, a diferencia de RedisMessageBodyRateLimiter) — comparte presupuesto entre réplicas. Fail-fast.</summary>
public sealed class RedisAttachmentRateLimiter(IRateCounter rateCounter, IOptions<AttachmentRateLimiterOptions> options)
    : IAttachmentRateLimiter
{
    public async Task<bool> TryAcquireAsync(Guid tenantId, CancellationToken ct = default)
    {
        var minuteBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var key = RateCounterKey.From($"connectors:attachment-fetch:{tenantId:N}:window:{minuteBucket}");

        var count = await rateCounter.IncrementAndGetAsync(key, TimeSpan.FromSeconds(65), ct);
        return count <= options.Value.MaxRequestsPerMinute;
    }
}
