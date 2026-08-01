using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Scribe.Application.Abstractions;

namespace TaxVision.Scribe.Infrastructure.Storage;

/// <summary>M2M puro (sin forward de bearer de usuario): el renderer de Scribe corre siempre en background.</summary>
public sealed class ServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<ServiceTokenAcquirer> logger
) : IServiceTokenAcquirer
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);

    private static readonly ExpiringValueCache<Guid, string> _cache = new(RefreshBuffer);

    // Defensa en profundidad ante una carrera de arranque de contenedores (auth-api todavía
    // aceptando conexiones cuando Scribe ya intenta pedir el token) — el ordering correcto lo
    // da docker-compose (depends_on auth-api: condition: service_healthy) más el gate de
    // ApplicationStarted en los callers (TemplateWarmupService/seeders), pero ninguno de los dos
    // cubre una reconexión/restart de auth-api DESPUÉS de que Scribe ya arrancó. Solo reintenta
    // fallos de conectividad (sin respuesta HTTP) — un 401/invalid_client sí llega como respuesta
    // y es un fallo permanente de credenciales, no algo que un retry vaya a arreglar.
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    ];

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId) || string.IsNullOrWhiteSpace(opt.ClientSecret))
        {
            logger.LogWarning("Scribe:ServiceAuth is not configured; cannot acquire a service token.");
            return null;
        }

        try
        {
            return await _cache.GetOrCreateAsync(
                tenantId,
                async innerCt =>
                {
                    var grant = await RequestWithRetryAsync(opt, tenantId, innerCt);
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

    private async Task<ServiceTokenGrant> RequestWithRetryAsync(
        ServiceAuthClientOptions opt,
        Guid tenantId,
        CancellationToken ct
    )
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await http.RequestServiceTokenAsync(opt.ClientId, opt.ClientSecret, tenantId, ct);
            }
            catch (ServiceTokenAcquisitionException ex) when (attempt < RetryDelays.Length && IsConnectivityFailure(ex))
            {
                logger.LogWarning(
                    ex,
                    "Service token request attempt {Attempt} failed for tenant {TenantId}; retrying in {Delay}.",
                    attempt + 1,
                    tenantId,
                    RetryDelays[attempt]
                );
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }
    }

    // HttpRequestException carries a null StatusCode when the request never got a response
    // (connection refused, DNS failure, etc.) and a populated StatusCode when Auth answered
    // with a non-2xx (e.g. 401 invalid_client) — only the former is worth retrying.
    private static bool IsConnectivityFailure(ServiceTokenAcquisitionException ex) =>
        ex.InnerException is HttpRequestException { StatusCode: null };
}
