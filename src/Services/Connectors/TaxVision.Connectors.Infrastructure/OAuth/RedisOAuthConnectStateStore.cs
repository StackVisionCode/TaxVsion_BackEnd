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
        string? initiatorEmail = null,
        string? returnOrigin = null,
        CancellationToken ct = default
    )
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        // 4º campo = email del usuario, 5º = origen de retorno (ni email ni una URL de origen
        // contienen '|'); vacíos si no vinieron.
        var value = $"{tenantId:N}|{providerCode}|{initiatedByUserId:N}|{initiatorEmail}|{returnOrigin}";
        await redis.GetDatabase().StringSetAsync(Key(state), value, Ttl);
        return state;
    }

    public async Task<OAuthConnectState?> ConsumeAsync(string state, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringGetDeleteAsync(Key(state));
        if (value.IsNullOrEmpty)
            return null;

        // Split sin límite: los states viejos (deploy en curso) traen 3-4 campos; los nuevos, 5.
        var parts = ((string)value!).Split('|');
        if (
            parts.Length < 3
            || !Guid.TryParse(parts[0], out var tenantId)
            || !Enum.TryParse<ProviderCode>(parts[1], out var providerCode)
            || !Guid.TryParse(parts[2], out var initiatedByUserId)
        )
            return null;

        var initiatorEmail = parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]) ? parts[3] : null;
        var returnOrigin = parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]) ? parts[4] : null;
        return new OAuthConnectState(tenantId, providerCode, initiatedByUserId, initiatorEmail, returnOrigin);
    }

    private static string Key(string state) => $"connectors:oauth-connect-state:{state}";
}
