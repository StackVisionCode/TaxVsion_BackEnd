using System.Security.Cryptography;
using StackExchange.Redis;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.OAuth;

/// <summary>
/// State del flujo de conectar cuenta en Redis — value pipe-delimited (<c>tenantId|providerCode|userId</c>,
/// sin JSON, mismo criterio minimalista que el resto de los value objects Redis del servicio).
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
        CancellationToken ct = default
    )
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var value = $"{tenantId:N}|{providerCode}|{initiatedByUserId:N}";
        await redis.GetDatabase().StringSetAsync(Key(state), value, Ttl);
        return state;
    }

    public async Task<OAuthConnectState?> ConsumeAsync(string state, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringGetDeleteAsync(Key(state));
        if (value.IsNullOrEmpty)
            return null;

        var parts = ((string)value!).Split('|', 3);
        if (
            parts.Length != 3
            || !Guid.TryParse(parts[0], out var tenantId)
            || !Enum.TryParse<ProviderCode>(parts[1], out var providerCode)
            || !Guid.TryParse(parts[2], out var initiatedByUserId)
        )
            return null;

        return new OAuthConnectState(tenantId, providerCode, initiatedByUserId);
    }

    private static string Key(string state) => $"connectors:oauth-connect-state:{state}";
}
