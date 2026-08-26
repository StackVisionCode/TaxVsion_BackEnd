using System.Text.Json;
using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Sesión de descubrimiento en Redis (TTL 60s). <c>PeekAsync</c> lee sin borrar (el MFA puede
/// reintentarse dentro de la ventana); <c>ConsumeAsync</c> borra cuando el ticket ya se emitió.
/// Misma dependencia de Redis crudo que los otros stores efímeros de Auth.
/// </summary>
public sealed class RedisDiscoverySessionStore(IConnectionMultiplexer redis) : IDiscoverySessionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Guid> StoreAsync(DiscoverySession session, CancellationToken ct = default)
    {
        var reference = Guid.NewGuid();
        var db = redis.GetDatabase();
        await db.StringSetAsync(Key(reference), JsonSerializer.Serialize(session, Json), Ttl);
        return reference;
    }

    public async Task<DiscoverySession?> PeekAsync(Guid reference, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(Key(reference));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<DiscoverySession>(value.ToString(), Json);
    }

    public Task ConsumeAsync(Guid reference, CancellationToken ct = default) =>
        redis.GetDatabase().KeyDeleteAsync(Key(reference));

    private static string Key(Guid reference) => $"auth:discovery-session:{reference:N}";
}
