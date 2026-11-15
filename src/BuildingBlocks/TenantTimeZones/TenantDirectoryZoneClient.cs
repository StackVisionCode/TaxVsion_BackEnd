using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.TenantTimeZones;

internal sealed class TenantDirectoryZoneClient(
    HttpClient httpClient,
    ITenantTimeZoneTokenProvider tokenProvider,
    ILogger<TenantDirectoryZoneClient> logger
) : ITenantDirectoryZoneClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<TenantDirectoryZone>?> ListPageAsync(int page, int size, CancellationToken ct = default)
    {
        var token = await tokenProvider.GetServiceTokenAsync(ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Time zone reconciliation aborted: could not acquire a service token.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/tenants/directory?page={page}&size={size}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Tenant directory returned {Status} on page {Page}.", (int)response.StatusCode, page);
                return null;
            }

            var items = await response.Content.ReadFromJsonAsync<List<ItemDto>>(Json, ct);
            return items?.Select(i => new TenantDirectoryZone(i.Id, i.DefaultTimeZoneId ?? string.Empty)).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Tenant directory request failed on page {Page}.", page);
            return null;
        }
    }

    private sealed record ItemDto(Guid Id, string? DefaultTimeZoneId);
}
