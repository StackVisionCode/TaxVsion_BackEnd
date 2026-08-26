using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TaxVision.Gateway.Middleware;

/// <summary>Veredicto de resolver un Host de oficina contra Auth.</summary>
public enum HostTenantOutcome
{
    /// <summary>El Host resuelve a un tenant registrado (<c>TenantId</c> poblado).</summary>
    Resolved,

    /// <summary>Auth respondió 404: el Host no está registrado como oficina.</summary>
    NotRegistered,

    /// <summary>No se pudo determinar (Auth caído/timeout). El caller decide — acá es fail-open.</summary>
    Unavailable,
}

public readonly record struct HostTenantResult(HostTenantOutcome Outcome, Guid TenantId);

public interface IHostTenantResolver
{
    Task<HostTenantResult> ResolveAsync(string host, CancellationToken ct = default);
}

/// <summary>
/// Resuelve Host→tenant llamando al <c>by-host</c> de Auth (que ya devuelve el <c>TenantId</c>),
/// reenviando el Host de oficina como header. Cachea en memoria: los subdominios casi nunca cambian,
/// así que evita una llamada por request. Nunca lanza: cualquier fallo de red/HTTP es
/// <see cref="HostTenantOutcome.Unavailable"/> y el middleware lo deja pasar (fail-open). Solo se
/// cachean respuestas definitivas de Auth — un <c>Unavailable</c> se reintenta en la próxima request.
/// </summary>
public sealed class HostTenantResolver(
    HttpClient httpClient,
    IOptions<TenantHostGuardOptions> options,
    ILogger<HostTenantResolver> logger
) : IHostTenantResolver
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private sealed record CacheEntry(HostTenantResult Result, DateTime ExpiresUtc);

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new();

    public async Task<HostTenantResult> ResolveAsync(string host, CancellationToken ct = default)
    {
        var key = host.ToLowerInvariant();
        if (Cache.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
            return cached.Result;

        var result = await FetchAsync(key, ct);

        if (result.Outcome != HostTenantOutcome.Unavailable)
        {
            var ttl =
                result.Outcome == HostTenantOutcome.Resolved
                    ? options.Value.PositiveCacheTtl
                    : options.Value.NegativeCacheTtl;
            Cache[key] = new CacheEntry(result, DateTime.UtcNow.Add(ttl));
        }

        return result;
    }

    private async Task<HostTenantResult> FetchAsync(string host, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "auth/tenant-resolution/by-host");
            // Auth resuelve el tenant candidato desde este Host (TenantHostResolutionMiddleware).
            request.Headers.Host = host;

            using var response = await httpClient.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new HostTenantResult(HostTenantOutcome.NotRegistered, Guid.Empty);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "by-host para {Host} devolvió {Status}; se trata como no disponible (fail-open).",
                    host,
                    (int)response.StatusCode
                );
                return new HostTenantResult(HostTenantOutcome.Unavailable, Guid.Empty);
            }

            var dto = await response.Content.ReadFromJsonAsync<ByHostDto>(Json, ct);
            if (dto is null || dto.TenantId == Guid.Empty)
                return new HostTenantResult(HostTenantOutcome.Unavailable, Guid.Empty);

            return new HostTenantResult(HostTenantOutcome.Resolved, dto.TenantId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo resolver el Host {Host} contra Auth; fail-open.", host);
            return new HostTenantResult(HostTenantOutcome.Unavailable, Guid.Empty);
        }
    }

    private sealed record ByHostDto(Guid TenantId);
}
