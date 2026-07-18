using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Providers;

namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>Fallback single-node cuando Redis no está configurado — mismo contrato que <see cref="RedisSendRateLimiter"/>, sin coordinación entre réplicas.</summary>
public sealed class InMemorySendRateLimiter(IOptions<SendRateLimiterOptions> options) : ISendRateLimiter
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid AccountId), (long Minute, int Count)> _windows = new();

    public Task<bool> TryAcquireAsync(Guid tenantId, Guid accountId, CancellationToken ct = default)
    {
        var key = (tenantId, accountId);
        var nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var updated = _windows.AddOrUpdate(
            key,
            _ => (nowMinute, 1),
            (_, existing) => existing.Minute == nowMinute ? (existing.Minute, existing.Count + 1) : (nowMinute, 1)
        );

        return Task.FromResult(updated.Count <= options.Value.MaxRequestsPerMinute);
    }
}
