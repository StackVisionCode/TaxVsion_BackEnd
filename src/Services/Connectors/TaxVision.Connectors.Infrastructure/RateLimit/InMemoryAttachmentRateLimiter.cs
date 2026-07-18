using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Providers;

namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>Fallback single-node cuando Redis no está configurado — mismo contrato que <see cref="RedisAttachmentRateLimiter"/>.</summary>
public sealed class InMemoryAttachmentRateLimiter(IOptions<AttachmentRateLimiterOptions> options)
    : IAttachmentRateLimiter
{
    private readonly ConcurrentDictionary<Guid, (long Minute, int Count)> _windows = new();

    public Task<bool> TryAcquireAsync(Guid tenantId, CancellationToken ct = default)
    {
        var nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var updated = _windows.AddOrUpdate(
            tenantId,
            _ => (nowMinute, 1),
            (_, existing) => existing.Minute == nowMinute ? (existing.Minute, existing.Count + 1) : (nowMinute, 1)
        );

        return Task.FromResult(updated.Count <= options.Value.MaxRequestsPerMinute);
    }
}
