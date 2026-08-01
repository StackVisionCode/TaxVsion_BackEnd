using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Postmaster.Infrastructure.Providers.Assets;

/// <summary>Adquisición y caché de tokens M2M (grant client-credentials) por tenant para llamar a CloudStorage.</summary>
public interface IPostmasterServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed class PostmasterServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<PostmasterServiceTokenAcquirer> logger
) : IPostmasterServiceTokenAcquirer
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);

    private static readonly ExpiringValueCache<Guid, string> _cache = new(RefreshBuffer);

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId) || string.IsNullOrWhiteSpace(opt.ClientSecret))
        {
            logger.LogWarning("Postmaster:ServiceAuth is not configured; cannot acquire a service token.");
            return null;
        }

        try
        {
            return await _cache.GetOrCreateAsync(
                tenantId,
                async innerCt =>
                {
                    var grant = await http.RequestServiceTokenAsync(opt.ClientId, opt.ClientSecret, tenantId, innerCt);
                    return (grant.AccessToken, grant.ExpiresAtUtc);
                },
                ct
            );
        }
        catch (ServiceTokenAcquisitionException ex)
        {
            logger.LogWarning(ex, "Could not acquire a service token for tenant {TenantId}.", tenantId);
            return null;
        }
    }
}
