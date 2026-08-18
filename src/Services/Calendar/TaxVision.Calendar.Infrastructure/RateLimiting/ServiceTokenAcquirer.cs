using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Calendar.Infrastructure.RateLimiting;

/// <summary>
/// <see cref="SectionName"/> tiene que casar exactamente con la clave de user-secrets y con la
/// variable de entorno (<c>Tasks__ServiceAuthClient__ClientSecret</c>). Un mismatch acá no rompe el
/// arranque: deja el acquirer devolviendo null en silencio.
/// </summary>
public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "ServiceAuthClient";

    /// <summary>Base URL del servicio Auth. En Docker: http://auth-api:8080.</summary>
    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Único acquirer M2M del servicio, con dos consumidores: <c>HttpPlanRateLimitReader</c> (catálogo
/// de PlanRateLimits de Subscription) y <c>PermissionsSnapshotClient</c> (recuperación pull de
/// permisos contra Auth). Ninguno necesita permisos en el cliente — la policy <c>ServiceOnly</c> de
/// ambos destinos solo exige <c>actor_type=Service</c>.
///
/// <para>
/// El cache es <c>static</c> a propósito: <c>AddHttpClient&lt;&gt;</c> registra esta clase como
/// transient, así que un campo de instancia se descartaría en cada resolución de DI y no cachearía
/// nada.
/// </para>
/// </summary>
internal sealed class ServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<ServiceTokenAcquirer> logger
) : IServiceTokenAcquirer
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
