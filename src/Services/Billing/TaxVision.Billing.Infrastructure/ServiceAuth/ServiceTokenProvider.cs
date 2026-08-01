using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Billing.Infrastructure.ServiceAuth;

/// <summary>Un solo proveedor de tokens M2M para todos los clientes de servicio de Billing
/// (documents, payments, …). Cada cliente tiene credenciales/audience propios (punto 10 del review:
/// no duplicar caché/renovación/locking por servicio), pero comparten esta lógica. El audience del
/// token lo decide Auth por el clientId, así que basta con (clientId, clientSecret).</summary>
public interface IServiceTokenProvider
{
    Task<string?> GetTokenAsync(string clientName, Guid tenantId, CancellationToken ct = default);
}

/// <summary>Credenciales de un cliente de servicio nombrado (bajo <c>Billing:ServiceClients:Clients</c>).</summary>
public sealed class ServiceClientCredentials
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class BillingServiceClientsOptions
{
    public const string SectionName = "Billing:ServiceClients";

    /// <summary>Base de Auth para el grant client-credentials (compartida por todos los clientes).</summary>
    public string AuthBaseUrl { get; set; } = "http://localhost:5124";

    /// <summary>Clientes por nombre (case-insensitive): "Documents", "Payments", …</summary>
    public Dictionary<string, ServiceClientCredentials> Clients { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ServiceTokenProvider(
    HttpClient http,
    IOptions<BillingServiceClientsOptions> options,
    ILogger<ServiceTokenProvider> logger
) : IServiceTokenProvider
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);

    // static: AddHttpClient<> resolves this class as Transient, so an instance field would be
    // recreated empty on every DI resolution and never actually cache anything.
    private static readonly ExpiringValueCache<(string ClientName, Guid TenantId), string> _cache = new(RefreshBuffer);

    public async Task<string?> GetTokenAsync(string clientName, Guid tenantId, CancellationToken ct = default)
    {
        if (!options.Value.Clients.TryGetValue(clientName, out var creds) || string.IsNullOrEmpty(creds.ClientId))
        {
            logger.LogWarning("No service-client credentials configured for '{ClientName}'.", clientName);
            return null;
        }

        try
        {
            return await _cache.GetOrCreateAsync(
                (clientName, tenantId),
                async innerCt =>
                {
                    var grant = await http.RequestServiceTokenAsync(
                        creds.ClientId,
                        creds.ClientSecret,
                        tenantId,
                        innerCt
                    );
                    return (grant.AccessToken, grant.ExpiresAtUtc);
                },
                ct
            );
        }
        catch (ServiceTokenAcquisitionException ex)
        {
            logger.LogWarning(
                ex,
                "Service-token request ({ClientName}) for tenant {TenantId} failed.",
                clientName,
                tenantId
            );
            return null;
        }
    }
}
