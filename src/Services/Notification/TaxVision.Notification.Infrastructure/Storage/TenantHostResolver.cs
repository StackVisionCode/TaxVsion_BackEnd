using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Storage;

/// <summary>
/// Pull M2M del host primario de un tenant contra el endpoint interno de Auth
/// (<c>internal/tenants/{tenantId}/primary-host</c>). Reusa el <see cref="IServiceTokenAcquirer"/>
/// que ya existe en este servicio — un acquirer, un HttpClient tipado por destino, igual que
/// <see cref="UserContactSnapshotClient"/>.
///
/// <para>
/// Cachea el host en <see cref="TenantHostCache"/> (TTL corto, y se invalida cuando la oficina renombra
/// su subdominio o la plataforma cambia de dominio): evita una llamada por cada correo. Nunca lanza: cualquier fallo de token, HTTP o 404 devuelve <c>null</c> y
/// el caller cae al base fijo de <c>PortalOptions</c> — un link degradado es mejor que un consumer que
/// revienta y se reintenta contra la DLQ.
/// </para>
/// </summary>
public sealed class TenantHostResolver(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    TenantHostCache cache,
    ILogger<TenantHostResolver> logger
) : ITenantHostResolver
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<string?> ResolveHostAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (cache.TryGet(tenantId, out var cached))
            return cached;

        var (host, _) = await FetchAsync(tenantId, ct);
        if (!string.IsNullOrWhiteSpace(host))
            cache.Set(tenantId, host!);

        return host;
    }

    // Links de cuenta: sin host lanza para que Wolverine reintente; nunca base fijo (D-04).
    public async Task<string> ResolveRequiredHostAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (cache.TryGet(tenantId, out var cached))
            return cached;

        var (host, failure) = await FetchAsync(tenantId, ct);
        if (!string.IsNullOrWhiteSpace(host))
        {
            cache.Set(tenantId, host!);
            return host!;
        }

        var reason = failure ?? TenantHostUnavailableReason.NoActiveSubdomain;
        logger.LogError(
            "tenant_host_unresolved: account link for tenant {TenantId} not sent ({Reason}); the message will be retried.",
            tenantId,
            reason
        );
        throw new TenantHostUnavailableException(tenantId, reason);
    }

    private async Task<(string? Host, TenantHostUnavailableReason? Failure)> FetchAsync(
        Guid tenantId,
        CancellationToken ct
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return (null, TenantHostUnavailableReason.Transient);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/tenants/{tenantId:D}/primary-host");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Primary-host pull for tenant {TenantId} returned {Status}.",
                    tenantId,
                    (int)response.StatusCode
                );
                // Auth responde 400/404 cuando la oficina no tiene subdominio activo; lo demás es transitorio.
                var noSubdomain = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest;
                return (
                    null,
                    noSubdomain ? TenantHostUnavailableReason.NoActiveSubdomain : TenantHostUnavailableReason.Transient
                );
            }

            var dto = await response.Content.ReadFromJsonAsync<HostDto>(Json, ct);
            return string.IsNullOrWhiteSpace(dto?.Host)
                ? (null, TenantHostUnavailableReason.NoActiveSubdomain)
                : (dto!.Host, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Primary-host pull call threw for tenant {TenantId}.", tenantId);
            return (null, TenantHostUnavailableReason.Transient);
        }
    }

    private sealed record HostDto(string Host);
}
