using BuildingBlocks.Infrastructure.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Providers;

namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>Ventana fija de 1 minuto por (tenant, cuenta) — comparte presupuesto entre réplicas. Fail-fast: nunca espera, a diferencia de IProviderRateLimiter.</summary>
public sealed class RedisMessageBodyRateLimiter(
    IRateCounter rateCounter,
    IOptions<MessageBodyRateLimiterOptions> options
) : IMessageBodyRateLimiter
{
    public async Task<bool> TryAcquireAsync(Guid tenantId, Guid accountId, CancellationToken ct = default)
    {
        var minuteBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var key = RateCounterKey.From($"connectors:body-fetch:{tenantId:N}:{accountId:N}:window:{minuteBucket}");

        var count = await rateCounter.IncrementAndGetAsync(key, TimeSpan.FromSeconds(65), ct);
        return count <= options.Value.MaxRequestsPerMinute;
    }
}
