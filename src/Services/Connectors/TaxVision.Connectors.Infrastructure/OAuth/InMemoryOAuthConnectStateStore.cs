using System.Collections.Concurrent;
using System.Security.Cryptography;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.OAuth;

/// <summary>Fallback single-node cuando Redis no está configurado — mismo contrato TTL+single-use que <see cref="RedisOAuthConnectStateStore"/>, sin coordinación entre réplicas.</summary>
public sealed class InMemoryOAuthConnectStateStore : IOAuthConnectStateStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (OAuthConnectState State, DateTime ExpiresAtUtc)> _states = new();

    public Task<string> CreateAsync(
        Guid tenantId,
        ProviderCode providerCode,
        Guid initiatedByUserId,
        CancellationToken ct = default
    )
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _states[state] = (new OAuthConnectState(tenantId, providerCode, initiatedByUserId), DateTime.UtcNow.Add(Ttl));
        return Task.FromResult(state);
    }

    public Task<OAuthConnectState?> ConsumeAsync(string state, CancellationToken ct = default)
    {
        if (!_states.TryRemove(state, out var entry))
            return Task.FromResult<OAuthConnectState?>(null);

        return Task.FromResult(entry.ExpiresAtUtc > DateTime.UtcNow ? entry.State : null);
    }
}
