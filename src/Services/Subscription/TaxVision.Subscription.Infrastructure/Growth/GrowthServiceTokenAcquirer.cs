using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Subscription.Infrastructure.Growth;

/// <summary>
/// Adquisición y caché de tokens M2M (grant client-credentials) por tenant contra Growth.
/// Mismo patrón que CorrespondenceServiceTokenAcquirer/SignatureServiceTokenAcquirer.
/// </summary>
internal interface IGrowthServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}

internal sealed class GrowthServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<GrowthServiceTokenAcquirer> logger
) : IGrowthServiceTokenAcquirer
{
    private static readonly ConcurrentDictionary<Guid, CachedToken> Cache = new();

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(tenantId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow.AddSeconds(30))
            return cached.Token;

        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId) || string.IsNullOrWhiteSpace(opt.ClientSecret))
        {
            logger.LogWarning("Subscription:ServiceAuth is not configured; cannot acquire a Growth service token.");
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
                logger.LogWarning("Growth service token request failed ({Status}).", (int)response.StatusCode);
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not acquire a Growth service token for tenant {TenantId}.", tenantId);
            return null;
        }
    }

    private sealed record CachedToken(string Token, DateTime ExpiresAtUtc);

    private sealed record ServiceTokenDto(string AccessToken, int ExpiresInSeconds, string? TokenType);
}
