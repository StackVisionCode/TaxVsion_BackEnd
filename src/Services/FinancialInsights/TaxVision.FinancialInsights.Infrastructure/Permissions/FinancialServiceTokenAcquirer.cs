using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TaxVision.FinancialInsights.Infrastructure.Permissions;

/// <summary>
/// Token M2M por tenant con caché hasta poco antes de vencer (<see cref="ExpiringValueCache{TKey,TValue}"/>, el
/// mismo patrón que el resto de servicios): una llamada a otro servicio no implica un POST a Auth.
/// </summary>
public sealed class FinancialServiceTokenAcquirer(
    HttpClient http,
    IConfiguration configuration,
    ILogger<FinancialServiceTokenAcquirer> logger
) : IServiceTokenAcquirer
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);

    // Estático: el cliente HTTP tipado es transitorio y la caché tiene que sobrevivirle.
    private static readonly ExpiringValueCache<Guid, string> Cache = new(RefreshBuffer);

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var clientId = configuration["ServiceAuthClient:ClientId"];
        var secret = configuration["ServiceAuthClient:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            return null;
        try
        {
            return await Cache.GetOrCreateAsync(
                tenantId,
                async innerCt =>
                {
                    var grant = await http.RequestServiceTokenAsync(clientId, secret, tenantId, innerCt);
                    return (grant.AccessToken, grant.ExpiresAtUtc);
                },
                ct
            );
        }
        catch (Exception ex)
            when (ex is ServiceTokenAcquisitionException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not acquire the FinancialInsights service token.");
            return null;
        }
    }
}
