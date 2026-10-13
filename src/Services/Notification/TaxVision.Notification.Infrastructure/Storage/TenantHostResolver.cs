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

        var host = await FetchAsync(tenantId, ct);
        if (!string.IsNullOrWhiteSpace(host))
            cache.Set(tenantId, host!);

        return host;
    }

    private async Task<string?> FetchAsync(Guid tenantId, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return null;

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
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<HostDto>(Json, ct);
            return string.IsNullOrWhiteSpace(dto?.Host) ? null : dto!.Host;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Primary-host pull call threw for tenant {TenantId}.", tenantId);
            return null;
        }
    }

    private sealed record HostDto(string Host);
}
