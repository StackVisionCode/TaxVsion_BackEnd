using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Observability;

namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>
/// Rate limiter con Redis para que N réplicas compartan el mismo presupuesto por provider.
/// Ventana fija de 1 segundo (INCR + EXPIRE) más un cooldown explícito activado por 429 real
/// (<see cref="RecordRateLimitedAsync"/>) — otros nodos lo ven de inmediato sin tener que
/// descubrir el 429 ellos mismos.
/// </summary>
public sealed class RedisProviderRateLimiter(IConnectionMultiplexer redis, IOptions<ProviderRateLimiterOptions> options)
    : IProviderRateLimiter
{
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromSeconds(60);

    public async Task WaitForSlotAsync(ProviderCode providerCode, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await WaitOutCooldownAsync(db, providerCode, ct);
        await WaitForWindowSlotAsync(db, providerCode, ct);
    }

    public async Task RecordRateLimitedAsync(
        ProviderCode providerCode,
        TimeSpan retryAfter,
        CancellationToken ct = default
    )
    {
        var capped = retryAfter > MaxCooldown ? MaxCooldown : retryAfter;
        await redis.GetDatabase().StringSetAsync(CooldownKey(providerCode), "1", capped);
        ConnectorsMetrics.RateLimitHits.Add(1, new KeyValuePair<string, object?>("provider", providerCode.ToString()));
    }

    private static async Task WaitOutCooldownAsync(IDatabase db, ProviderCode providerCode, CancellationToken ct)
    {
        var remaining = await db.KeyTimeToLiveAsync(CooldownKey(providerCode));
        if (remaining is { } ttl && ttl > TimeSpan.Zero)
            await Task.Delay(ttl, ct);
    }

    private async Task WaitForWindowSlotAsync(IDatabase db, ProviderCode providerCode, CancellationToken ct)
    {
        while (true)
        {
            var windowKey = WindowKey(providerCode, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var count = await db.StringIncrementAsync(windowKey);
            if (count == 1)
                await db.KeyExpireAsync(windowKey, TimeSpan.FromSeconds(2));

            if (count <= options.Value.MaxRequestsPerSecond)
                return;

            var msIntoSecond = DateTimeOffset.UtcNow.Millisecond;
            await Task.Delay(1000 - msIntoSecond + 5, ct);
        }
    }

    private static string CooldownKey(ProviderCode providerCode) => $"connectors:ratelimit:{providerCode}:cooldown";

    private static string WindowKey(ProviderCode providerCode, long unixSecond) =>
        $"connectors:ratelimit:{providerCode}:window:{unixSecond}";
}
