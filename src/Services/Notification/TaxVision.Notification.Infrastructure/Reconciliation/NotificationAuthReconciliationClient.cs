using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;

namespace TaxVision.Notification.Infrastructure.Reconciliation;

internal sealed class NotificationAuthReconciliationClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<NotificationAuthReconciliationClient> logger
) : INotificationAuthReconciliationClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<RemotePage<RemoteTenantOwner>?> ListOwnersAsync(
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var dto = await GetPageAsync<OwnerDto>(
            $"internal/users/owners/reconciliation?page={page}&size={size}",
            page,
            ct
        );
        return dto is null
            ? null
            : new RemotePage<RemoteTenantOwner>(
                dto.Items.Select(x => new RemoteTenantOwner(x.TenantId, x.UserId, x.RegisteredAtUtc, x.IsActive))
                    .ToList(),
                dto.Page * dto.Size < dto.TotalCount
            );
    }

    public async Task<RemotePage<RemotePortalAccount>?> ListPortalAccountsAsync(
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var dto = await GetPageAsync<PortalDto>(
            $"internal/users/portal-accounts/reconciliation?page={page}&size={size}",
            page,
            ct
        );
        return dto is null
            ? null
            : new RemotePage<RemotePortalAccount>(
                dto.Items.Select(x => new RemotePortalAccount(x.TenantId, x.CustomerId, x.UserId, x.IsActive)).ToList(),
                dto.Page * dto.Size < dto.TotalCount
            );
    }

    private async Task<PageDto<T>?> GetPageAsync<T>(string path, int page, CancellationToken ct)
    {
        var token = await tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Recipient projection reconciliation aborted: no PlatformTenant service token.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Recipient projection reconciliation failed ({Status}) on page {Page}.",
                    (int)response.StatusCode,
                    page
                );
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PageDto<T>>(Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Recipient projection reconciliation could not reach Auth on page {Page}.", page);
            return null;
        }
    }

    private sealed record PageDto<T>(IReadOnlyList<T> Items, int Page, int Size, int TotalCount);

    private sealed record OwnerDto(Guid TenantId, Guid UserId, DateTime RegisteredAtUtc, bool IsActive);

    private sealed record PortalDto(Guid TenantId, Guid CustomerId, Guid UserId, bool IsActive);
}
