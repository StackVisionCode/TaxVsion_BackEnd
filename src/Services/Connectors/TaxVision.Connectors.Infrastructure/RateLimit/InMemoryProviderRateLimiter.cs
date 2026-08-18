using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Observability;

namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>Fallback single-node cuando Redis no está configurado — mismo contrato de ventana global+per-tenant+cooldown que <see cref="RedisProviderRateLimiter"/> (Rate Limit Fase 0.3), sin coordinación entre réplicas.</summary>
public sealed class InMemoryProviderRateLimiter(IOptions<ProviderRateLimiterOptions> options) : IProviderRateLimiter
{
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<ProviderCode, DateTime> _cooldownUntilUtc = new();
    private readonly ConcurrentDictionary<ProviderCode, (long Second, int Count)> _globalWindows = new();
    private readonly ConcurrentDictionary<
        (ProviderCode Provider, Guid TenantId),
        (long Second, int Count)
    > _tenantWindows = new();

    public async Task WaitForSlotAsync(ProviderCode providerCode, Guid tenantId, CancellationToken ct = default)
    {
        if (_cooldownUntilUtc.TryGetValue(providerCode, out var until) && until > DateTime.UtcNow)
            await Task.Delay(until - DateTime.UtcNow, ct);

        while (true)
        {
            var nowSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var globalUpdated = _globalWindows.AddOrUpdate(
                providerCode,
                _ => (nowSecond, 1),
                (_, existing) => existing.Second == nowSecond ? (existing.Second, existing.Count + 1) : (nowSecond, 1)
            );
            var tenantUpdated = _tenantWindows.AddOrUpdate(
                (providerCode, tenantId),
                _ => (nowSecond, 1),
                (_, existing) => existing.Second == nowSecond ? (existing.Second, existing.Count + 1) : (nowSecond, 1)
            );

            if (
                globalUpdated.Count <= options.Value.MaxRequestsPerSecond
                && tenantUpdated.Count <= options.Value.MaxRequestsPerSecondPerTenant
            )
                return;

            var msIntoSecond = DateTimeOffset.UtcNow.Millisecond;
            await Task.Delay(1000 - msIntoSecond + 5, ct);
        }
    }

    public Task RecordRateLimitedAsync(ProviderCode providerCode, TimeSpan retryAfter, CancellationToken ct = default)
    {
        var capped = retryAfter > MaxCooldown ? MaxCooldown : retryAfter;
        _cooldownUntilUtc[providerCode] = DateTime.UtcNow.Add(capped);
        ConnectorsMetrics.RateLimitHits.Add(1, new KeyValuePair<string, object?>("provider", providerCode.ToString()));
        return Task.CompletedTask;
    }
}
