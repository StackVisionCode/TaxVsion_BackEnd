using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Staff.Abstractions;

namespace TaxVision.Calendar.Infrastructure.Staff;

/// <summary>
/// Host de la oficina desde Auth (<c>internal/tenants/{t}/primary-host</c>, M2M), como en Notification.
/// Treinta minutos en memoria: cambia casi nunca. Nunca lanza.
/// </summary>
public sealed class TenantHostResolver(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    IMemoryCache cache,
    ILogger<TenantHostResolver> logger
) : ITenantHostResolver
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<string?> ResolveHostAsync(Guid tenantId, CancellationToken ct = default)
    {
        var key = $"calendar:tenant-host:{tenantId:N}";
        if (cache.TryGetValue(key, out string? cached))
            return cached;

        var host = await FetchAsync(tenantId, ct);
        if (!string.IsNullOrWhiteSpace(host))
            cache.Set(key, host, Ttl);
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
                    "Primary host for tenant {TenantId} returned {Status}.",
                    tenantId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<HostDto>(Json, ct);
            return string.IsNullOrWhiteSpace(dto?.Host) ? null : dto.Host;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Primary host call threw for tenant {TenantId}.", tenantId);
            return null;
        }
    }

    private sealed record HostDto(string Host);
}
