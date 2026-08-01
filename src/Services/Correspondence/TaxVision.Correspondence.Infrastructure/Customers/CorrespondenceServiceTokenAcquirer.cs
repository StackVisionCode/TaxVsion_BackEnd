using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Correspondence.Infrastructure.Customers;

/// <summary>
/// Adquisición y caché de tokens M2M (grant client-credentials) por tenant. Alimenta a
/// <see cref="CorrespondenceCustomerClient"/> cuando el backfill de un tenant recién descubierto
/// necesita hablar con Customer.Api sin un request de usuario. Mismo patrón que
/// SignatureServiceTokenAcquirer/PostmasterServiceTokenAcquirer (F25: todos componen
/// <see cref="ExpiringValueCache{TKey,TValue}"/> + <see cref="ServiceTokenHttpAcquisition"/>, ambos
/// en BuildingBlocks).
/// </summary>
internal interface ICorrespondenceServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}

internal sealed class CorrespondenceServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<CorrespondenceServiceTokenAcquirer> logger
) : ICorrespondenceServiceTokenAcquirer
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);

    private static readonly ExpiringValueCache<Guid, string> _cache = new(RefreshBuffer);

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId) || string.IsNullOrWhiteSpace(opt.ClientSecret))
        {
            logger.LogWarning("Correspondence:ServiceAuth is not configured; cannot acquire a service token.");
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
