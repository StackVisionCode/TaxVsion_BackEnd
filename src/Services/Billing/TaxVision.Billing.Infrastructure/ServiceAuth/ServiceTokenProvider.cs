using System.Collections.Concurrent;
using System.Net.Http.Json;
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
    public Dictionary<string, ServiceClientCredentials> Clients { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ServiceTokenProvider(
    HttpClient http,
    IOptions<BillingServiceClientsOptions> options,
    ILogger<ServiceTokenProvider> logger
) : IServiceTokenProvider
{
    private readonly ConcurrentDictionary<string, CachedToken> _cache = new();

    public async Task<string?> GetTokenAsync(string clientName, Guid tenantId, CancellationToken ct = default)
    {
        var cacheKey = $"{clientName}:{tenantId}";
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow.AddSeconds(30))
            return cached.Token;

        if (!options.Value.Clients.TryGetValue(clientName, out var creds) || string.IsNullOrEmpty(creds.ClientId))
        {
            logger.LogWarning("No service-client credentials configured for '{ClientName}'.", clientName);
            return null;
        }

        try
        {
            using var response = await http.PostAsJsonAsync(
                "auth/service-token",
                new { clientId = creds.ClientId, clientSecret = creds.ClientSecret, tenantId },
                ct
            );

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Service-token request ({ClientName}) for tenant {TenantId} failed with {Status}.",
                    clientName,
                    tenantId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<ServiceTokenDto>(ct);
            if (dto is null || string.IsNullOrEmpty(dto.AccessToken))
                return null;

            _cache[cacheKey] = new CachedToken(dto.AccessToken, DateTime.UtcNow.AddSeconds(dto.ExpiresInSeconds));
            return dto.AccessToken;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Service-token request ({ClientName}) for tenant {TenantId} threw.", clientName, tenantId);
            return null;
        }
    }

    private sealed record CachedToken(string Token, DateTime ExpiresAtUtc);

    private sealed record ServiceTokenDto(string AccessToken, int ExpiresInSeconds, string? TokenType);
}
