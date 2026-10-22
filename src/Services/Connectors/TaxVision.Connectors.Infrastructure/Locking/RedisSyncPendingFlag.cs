using StackExchange.Redis;
using TaxVision.Connectors.Application.Abstractions;

namespace TaxVision.Connectors.Infrastructure.Locking;

public sealed class RedisSyncPendingFlag(IConnectionMultiplexer redis) : ISyncPendingFlag
{
    /// <summary>Si nadie la consume (el dueño murió), la reconciliación cubre igual; no hace falta que dure más.</summary>
    private static readonly TimeSpan FlagTtl = TimeSpan.FromMinutes(15);

    public Task MarkAsync(Guid accountId, CancellationToken ct = default) =>
        redis.GetDatabase().StringSetAsync(Key(accountId), 1, FlagTtl);

    // DEL devuelve si existía: leer y borrar en un solo paso, sin carrera entre dos dueños.
    public Task<bool> TryConsumeAsync(Guid accountId, CancellationToken ct = default) =>
        redis.GetDatabase().KeyDeleteAsync(Key(accountId));

    private static string Key(Guid accountId) => $"connectors:sync-pending:{accountId:N}";
}
