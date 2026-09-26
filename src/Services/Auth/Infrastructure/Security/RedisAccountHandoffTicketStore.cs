using System.Text.Json;
using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>Vale del Account en Redis: TTL de 60 s y GETDEL para el canje de un solo uso.</summary>
public sealed class RedisAccountHandoffTicketStore(IConnectionMultiplexer redis) : IAccountHandoffTicketStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TimeSpan Lifetime { get; } = TimeSpan.FromSeconds(60);

    public async Task<Guid> IssueAsync(AccountHandoffPayload payload, CancellationToken ct = default)
    {
        var ticket = Guid.NewGuid();
        await redis.GetDatabase().StringSetAsync(Key(ticket), JsonSerializer.Serialize(payload, Json), Lifetime);
        return ticket;
    }

    public async Task<AccountHandoffPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringGetDeleteAsync(Key(ticket));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<AccountHandoffPayload>(value.ToString(), Json);
    }

    private static string Key(Guid ticket) => $"auth:account-handoff:{ticket:N}";
}
