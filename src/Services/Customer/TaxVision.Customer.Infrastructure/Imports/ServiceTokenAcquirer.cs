using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Customer.Infrastructure.Imports;

public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "ServiceAuthClient";

    /// <summary>Base URL del servicio Auth. En Docker: http://auth-api:8080.</summary>
    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

internal interface IServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Obtiene tokens de servicio (M2M) de Auth (grant client-credentials) para un tenant y los
/// cachea hasta poco antes de expirar. Usado por el worker de import para autenticar contra
/// CloudStorage (download/delete) sin contexto de usuario.
/// </summary>
internal sealed class ServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<ServiceTokenAcquirer> logger
) : IServiceTokenAcquirer
{
    private static readonly ConcurrentDictionary<Guid, CachedToken> Cache = new();

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(tenantId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow.AddSeconds(30))
            return cached.Token;

        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId) || string.IsNullOrWhiteSpace(opt.ClientSecret))
        {
            logger.LogWarning("ServiceAuthClient is not configured; cannot acquire a service token.");
            return null;
        }

        try
        {
            using var response = await http.PostAsJsonAsync(
                "auth/service-token",
                new
                {
                    clientId = opt.ClientId,
                    clientSecret = opt.ClientSecret,
                    tenantId,
                },
                ct
            );
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Service token request failed ({Status}).", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<ServiceTokenDto>(ct);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
                return null;

            Cache[tenantId] = new CachedToken(
                payload.AccessToken,
                DateTime.UtcNow.AddSeconds(payload.ExpiresInSeconds)
            );
            return payload.AccessToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not acquire a service token for tenant {TenantId}.", tenantId);
            return null;
        }
    }

    private sealed record CachedToken(string Token, DateTime ExpiresAtUtc);

    private sealed record ServiceTokenDto(string AccessToken, int ExpiresInSeconds, string? TokenType);
}
