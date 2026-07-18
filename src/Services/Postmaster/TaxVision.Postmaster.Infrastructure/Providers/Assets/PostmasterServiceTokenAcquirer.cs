using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Postmaster.Infrastructure.Providers.Assets;

/// <summary>Adquisición y caché de tokens M2M (grant client-credentials) por tenant para llamar a CloudStorage.</summary>
public interface IPostmasterServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed class PostmasterServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<PostmasterServiceTokenAcquirer> logger
) : IPostmasterServiceTokenAcquirer
{
    private static readonly ConcurrentDictionary<Guid, CachedToken> Cache = new();

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(tenantId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow.AddSeconds(30))
            return cached.Token;

        var opt = options.Value;
        if (!AreCredentialsConfigured(opt))
        {
            logger.LogWarning("Postmaster:ServiceAuth is not configured; cannot acquire a service token.");
            return null;
        }

        try
        {
            var payload = await RequestTokenAsync(opt, tenantId, ct);
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

    private static bool AreCredentialsConfigured(ServiceAuthClientOptions opt) =>
        !string.IsNullOrWhiteSpace(opt.ClientId) && !string.IsNullOrWhiteSpace(opt.ClientSecret);

    private async Task<ServiceTokenDto?> RequestTokenAsync(
        ServiceAuthClientOptions opt,
        Guid tenantId,
        CancellationToken ct
    )
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
        return await response.Content.ReadFromJsonAsync<ServiceTokenDto>(ct);
    }

    private sealed record CachedToken(string Token, DateTime ExpiresAtUtc);

    private sealed record ServiceTokenDto(string AccessToken, int ExpiresInSeconds, string? TokenType);
}
