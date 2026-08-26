using System.Text.Json;
using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Vale de handoff en Redis: <c>StringSetAsync</c> con TTL de 60s y <c>StringGetDeleteAsync</c>
/// (GETDEL atómico) para el consumo de un solo uso — misma mecánica que
/// <see cref="RedisTokenReferenceStore"/>, separada porque el vale lleva una identidad tipada
/// (tenant + user), no un raw token, y su ventana de vida es propia.
/// </summary>
public sealed class RedisHandoffTicketStore(IConnectionMultiplexer redis) : IHandoffTicketStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Guid> IssueAsync(HandoffTicketPayload payload, CancellationToken ct = default)
    {
        var ticket = Guid.NewGuid();
        var db = redis.GetDatabase();
        await db.StringSetAsync(Key(ticket), JsonSerializer.Serialize(payload, Json), Ttl);
        return ticket;
    }

    public async Task<HandoffTicketPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetDeleteAsync(Key(ticket));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<HandoffTicketPayload>(value.ToString(), Json);
    }

    private static string Key(Guid ticket) => $"auth:handoff-ticket:{ticket:N}";
}
