using System.Collections.Concurrent;
using System.Net.Http.Json;
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
    private static readonly ConcurrentDictionary<Guid, CachedToken> Cache = new();

    // Defensa en profundidad ante una carrera de arranque de contenedores (auth-api todavía
    // aceptando conexiones cuando Scribe ya intenta pedir el token) — el ordering correcto lo
    // da docker-compose (depends_on auth-api: condition: service_healthy) más el gate de
    // ApplicationStarted en los callers (TemplateWarmupService/seeders), pero ninguno de los dos
    // cubre una reconexión/restart de auth-api DESPUÉS de que Scribe ya arrancó. Solo reintenta
    // fallos de conectividad (HttpRequestException) — un 401/invalid_client es un fallo
    // permanente de credenciales, no algo que un retry vaya a arreglar.
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    ];

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(tenantId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow.AddSeconds(30))
            return cached.Token;

        var opt = options.Value;
        if (!AreCredentialsConfigured(opt))
        {
            logger.LogWarning("Scribe:ServiceAuth is not configured; cannot acquire a service token.");
            return null;
        }

        for (var attempt = 0; ; attempt++)
        {
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
            catch (HttpRequestException ex) when (attempt < RetryDelays.Length)
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
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not acquire a service token for tenant {TenantId}.", tenantId);
                return null;
            }
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
