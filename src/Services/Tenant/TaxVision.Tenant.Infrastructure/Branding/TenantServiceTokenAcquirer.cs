using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Tenant.Infrastructure.Branding;

internal interface ITenantServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Obtiene tokens de servicio (M2M) de Auth (grant client-credentials) para un tenant y los cachea
/// hasta poco antes de expirar. Usado para autenticar contra CloudStorage (download-url/delete del
/// logo) sin contexto de usuario — mismo patrón que Customer/Signature/Scribe/Correspondence/
/// Notification/Postmaster/Growth/PaymentApp (F25: todos componen <see cref="ExpiringValueCache{TKey,TValue}"/>
/// + <see cref="ServiceTokenHttpAcquisition"/>, ambos en BuildingBlocks). Implementa también
/// <see cref="BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer"/> (RateLimit Fase 2) para
/// que <c>HttpPlanRateLimitReader</c> (compartido) pueda consumir este mismo acquirer.
/// </summary>
internal sealed class TenantServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<TenantServiceTokenAcquirer> logger
) : ITenantServiceTokenAcquirer, BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer
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
