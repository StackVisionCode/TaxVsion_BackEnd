using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.PaymentApp.Infrastructure.Subscriptions;

internal interface IPaymentAppServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Obtiene tokens de servicio (M2M) de Auth (client-credentials) y los cachea hasta poco
/// antes de expirar. Mismo patrón que TenantServiceTokenAcquirer/CustomerServiceTokenAcquirer/etc.</summary>
internal sealed class PaymentAppServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<PaymentAppServiceTokenAcquirer> logger
) : IPaymentAppServiceTokenAcquirer
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);

    private static readonly ExpiringValueCache<Guid, string> _cache = new(RefreshBuffer);

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId) || string.IsNullOrWhiteSpace(opt.ClientSecret))
        {
            logger.LogWarning("ServiceAuthClient is not configured; cannot acquire a service token.");
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
