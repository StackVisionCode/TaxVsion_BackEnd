using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Infrastructure.Growth;

namespace TaxVision.Subscription.Infrastructure.CloudStorage;

/// <summary>Base URL de CloudStorage para las lecturas M2M de Subscription.</summary>
public sealed class CloudStorageClientOptions
{
    public const string SectionName = "Subscription:CloudStorage";

    public string BaseUrl { get; set; } = "http://localhost:5330";
}

/// <summary>
/// D11: cuánto almacenamiento usa una oficina (<c>GET internal/storage/usage</c> de CloudStorage, token de servicio
/// emitido para esa oficina). Si CloudStorage no responde devuelve null y la migración bloquea la oficina.
/// </summary>
internal sealed class CloudStorageUsageClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    ILogger<CloudStorageUsageClient> logger
) : IStorageUsageClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<long?> GetUsedBytesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Could not acquire a service token to read the storage usage of {TenantId}.", tenantId);
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "internal/storage/usage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "CloudStorage usage for {TenantId} returned {Status}.",
                    tenantId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<UsageDto>(Json, ct);
            return dto?.UsedBytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "CloudStorage usage call threw for tenant {TenantId}.", tenantId);
            return null;
        }
    }

    private sealed record UsageDto(Guid TenantId, long UsedBytes, long ReservedBytes);
}
