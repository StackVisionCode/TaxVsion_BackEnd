using System.Security.Cryptography;
using StackExchange.Redis;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.OAuth;

/// <summary>
/// State del flujo de conectar cuenta en Redis, codificado con <see cref="OAuthConnectStateCodec"/>.
/// <c>StringGetDeleteAsync</c> (GETDEL) hace el consumo atómico — nunca hay ventana donde dos
/// callbacks concurrentes con el mismo state puedan consumirlo dos veces.
/// </summary>
public sealed class RedisOAuthConnectStateStore(IConnectionMultiplexer redis) : IOAuthConnectStateStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async Task<string> CreateAsync(
        Guid tenantId,
        ProviderCode providerCode,
        Guid initiatedByUserId,
        string? initiatorEmail = null,
        string? returnOrigin = null,
        bool asOffice = false,
        bool initiatorIsPlatformAdmin = false,
        CancellationToken ct = default
    )
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var value = OAuthConnectStateCodec.Encode(
            new OAuthConnectState(
                tenantId,
                providerCode,
                initiatedByUserId,
                initiatorEmail,
                returnOrigin,
                asOffice,
                initiatorIsPlatformAdmin
            )
        );
        await redis.GetDatabase().StringSetAsync(Key(state), value, Ttl);
        return state;
    }

    public async Task<OAuthConnectState?> ConsumeAsync(string state, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringGetDeleteAsync(Key(state));
        return value.IsNullOrEmpty ? null : OAuthConnectStateCodec.Decode(value);
    }

    private static string Key(string state) => $"connectors:oauth-connect-state:{state}";
}
