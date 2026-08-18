using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Necesita GETDEL atómico (borrar exactamente al leer, TTL corto de 30s) que <c>ICacheService</c>
/// (el wrapper de <c>IDistributedCache</c> que Auth ya usa en <c>LoginThrottler</c>) no
/// puede dar — Get+Remove por separado no es atómico. Por eso este es el primer uso de
/// <see cref="IConnectionMultiplexer"/> crudo en Auth, igual que <c>RedisOAuthConnectStateStore</c>
/// en Connectors. Originado en PayFlow Fase 9 para el RegistrationToken de Onboarding; Fase 18 lo
/// reusa para el token de activación del TenantAdmin — de ahí que ya no viva bajo Infrastructure/Onboarding.
/// </summary>
public sealed class RedisTokenReferenceStore(IConnectionMultiplexer redis) : ITokenReferenceStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public async Task<Guid> StoreAsync(string rawToken, CancellationToken ct = default)
    {
        var reference = Guid.NewGuid();
        var db = redis.GetDatabase();
        await db.StringSetAsync(Key(reference), rawToken, Ttl);
        return reference;
    }

    public async Task<string?> ConsumeAsync(Guid reference, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetDeleteAsync(Key(reference));
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public async Task<string?> PeekAsync(Guid reference, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(Key(reference));
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    private static string Key(Guid reference) => $"auth:token-reference:{reference:N}";
}
