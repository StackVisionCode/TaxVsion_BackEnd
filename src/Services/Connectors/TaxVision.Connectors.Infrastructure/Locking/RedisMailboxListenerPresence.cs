using StackExchange.Redis;
using TaxVision.Connectors.Application.Abstractions;

namespace TaxVision.Connectors.Infrastructure.Locking;

public sealed class RedisMailboxListenerPresence(IConnectionMultiplexer redis) : IMailboxListenerPresence
{
    public Task MarkListeningAsync(Guid accountId, TimeSpan ttl, CancellationToken ct = default) =>
        redis.GetDatabase().StringSetAsync(AliveKey(accountId), 1, ttl);

    public Task<bool> IsListeningAsync(Guid accountId, CancellationToken ct = default) =>
        redis.GetDatabase().KeyExistsAsync(AliveKey(accountId));

    public Task<bool> TryClaimSafetyNetSlotAsync(Guid accountId, TimeSpan interval, CancellationToken ct = default) =>
        redis
            .GetDatabase()
            .StringSetAsync($"connectors:listener:safety-net:{accountId:N}", 1, interval, When.NotExists);

    private static string AliveKey(Guid accountId) => $"connectors:listener:alive:{accountId:N}";
}
