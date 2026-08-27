using System.Text.Json;
using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Vale de takeover en Redis: <c>StringSetAsync</c> con TTL de 2 min y <c>StringGetDeleteAsync</c>
/// (GETDEL atómico) para el consumo de un solo uso — misma mecánica que
/// <see cref="RedisHandoffTicketStore"/>, separada porque lleva una identidad tipada propia y su
/// ventana de vida (el tiempo que el usuario tarda en decidir en el interstitial) es distinta.
/// </summary>
public sealed class RedisSessionTakeoverTicketStore(IConnectionMultiplexer redis) : ISessionTakeoverTicketStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Guid> IssueAsync(SessionTakeoverPayload payload, CancellationToken ct = default)
    {
        var ticket = Guid.NewGuid();
        var db = redis.GetDatabase();
        await db.StringSetAsync(Key(ticket), JsonSerializer.Serialize(payload, Json), Ttl);
        return ticket;
    }

    public async Task<SessionTakeoverPayload?> ConsumeAsync(Guid ticket, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetDeleteAsync(Key(ticket));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<SessionTakeoverPayload>(value.ToString(), Json);
    }

    private static string Key(Guid ticket) => $"auth:session-takeover-ticket:{ticket:N}";
}
