using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Storage;

public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "ServiceAuthClient";

    /// <summary>Base URL del servicio Auth. En Docker: http://auth-api:8080.</summary>
    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Obtiene tokens de servicio (M2M) del Auth (grant client-credentials) para un tenant y los cachea
/// hasta poco antes de expirar. Usado por el worker de sincronización para autenticar contra CloudStorage
/// sin contexto de usuario. Implementa tanto el puerto local de Application (dueño del contrato para
/// los consumers internos de Notification) como <see cref="BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer"/>
/// — RateLimit Fase 2 lo necesita para que <c>HttpPlanRateLimitReader</c> (compartido) pueda
/// consumir este mismo acquirer sin que Notification duplique la lógica de cache+retry.
/// </summary>
public sealed class ServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<ServiceTokenAcquirer> logger
)
    : TaxVision.Notification.Application.Abstractions.IServiceTokenAcquirer,
        BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer
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
