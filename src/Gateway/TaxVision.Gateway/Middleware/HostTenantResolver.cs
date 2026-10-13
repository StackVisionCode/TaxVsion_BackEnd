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
/// Respuestas definitivas de Auth por host, compartidas por todas las instancias del resolver (que es un
/// typed HttpClient, transitorio). Singleton.
/// </summary>
public sealed class HostTenantCache
{
    public sealed record Entry(HostTenantResult Result, DateTime FetchedUtc);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, byte> _revalidating = new();

    public bool TryGet(string host, out Entry entry) => _entries.TryGetValue(host, out entry!);

    public void Set(string host, HostTenantResult result, DateTime nowUtc) =>
        _entries[host] = new Entry(result, nowUtc);

    /// <summary>Marca el host como "revalidándose". Falso si ya hay una revalidación en curso.</summary>
    public bool TryBeginRevalidation(string host) => _revalidating.TryAdd(host, 0);

    public void EndRevalidation(string host) => _revalidating.TryRemove(host, out _);
}

/// <summary>
/// Resuelve Host→tenant llamando al <c>by-host</c> de Auth (que ya devuelve el <c>TenantId</c>),
/// reenviando el Host de oficina como header. Nunca lanza: cualquier fallo de red/HTTP es
/// <see cref="HostTenantOutcome.Unavailable"/> y el middleware lo deja pasar (fail-open).
///
/// <para>Cache stale-while-revalidate: una entrada positiva se sirve al instante, y si tiene más de
/// <see cref="TenantHostGuardOptions.RevalidateAfter"/> se vuelve a pedir a Auth en segundo plano. Así un
/// renombre o un dominio deshabilitado se nota en segundos sin evento (el Gateway no usa el bus: no puede
/// depender de RabbitMQ para arrancar) y sin sumar latencia. <see cref="TenantHostGuardOptions.PositiveCacheTtl"/>
/// es el tope si Auth no contesta. Un <c>Unavailable</c> no se cachea nunca.</para>
/// </summary>
public sealed class HostTenantResolver(
    HttpClient httpClient,
    HostTenantCache cache,
    IOptions<TenantHostGuardOptions> options,
    TimeProvider clock,
    ILogger<HostTenantResolver> logger
) : IHostTenantResolver
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<HostTenantResult> ResolveAsync(string host, CancellationToken ct = default)
    {
        var key = host.ToLowerInvariant();
        var now = clock.GetUtcNow().UtcDateTime;
        var settings = options.Value;

        if (cache.TryGet(key, out var cached))
        {
            var age = now - cached.FetchedUtc;
            var resolved = cached.Result.Outcome == HostTenantOutcome.Resolved;
            var ttl = resolved ? settings.PositiveCacheTtl : settings.NegativeCacheTtl;
            if (age < ttl)
            {
                if (resolved && age >= settings.RevalidateAfter && cache.TryBeginRevalidation(key))
                    _ = RevalidateAsync(key);
                return cached.Result;
            }
        }

        var result = await FetchAsync(key, ct);
        Remember(key, result);
        return result;
    }

    private async Task RevalidateAsync(string host)
    {
        try
        {
            // Fuera del request: no se cancela si el cliente corta. El timeout del HttpClient lo acota.
            var result = await FetchAsync(host, CancellationToken.None);
            Remember(host, result);
        }
        finally
        {
            cache.EndRevalidation(host);
        }
    }

    private void Remember(string host, HostTenantResult result)
    {
        if (result.Outcome != HostTenantOutcome.Unavailable)
            cache.Set(host, result, clock.GetUtcNow().UtcDateTime);
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
