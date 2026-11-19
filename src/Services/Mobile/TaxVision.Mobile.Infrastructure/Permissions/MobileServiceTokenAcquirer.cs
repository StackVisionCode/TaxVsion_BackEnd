using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TaxVision.Mobile.Infrastructure.Permissions;

public sealed class MobileServiceTokenAcquirer(
    HttpClient http,
    IConfiguration configuration,
    ILogger<MobileServiceTokenAcquirer> logger
) : IServiceTokenAcquirer
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);
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
            when (!ct.IsCancellationRequested
                && ex is ServiceTokenAcquisitionException or HttpRequestException or TaskCanceledException
            )
        {
            logger.LogWarning(ex, "Could not acquire the Mobile service token.");
            return null;
        }
    }
}
