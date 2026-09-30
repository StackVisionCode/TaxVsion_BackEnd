using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Infrastructure.Growth;

namespace TaxVision.Subscription.Infrastructure.Auth;

/// <summary>
/// Implementación de <see cref="ITenantUserCountClient"/> contra
/// <c>GET internal/tenants/{tenantId}/user-count</c> de Auth (M2M ServiceOnly). Molde:
/// <c>PaymentAppSeatCheckoutClient</c>. Si Auth no responde devuelve null; el guard de downgrade decide.
/// </summary>
internal sealed class AuthTenantUserCountClient(
    HttpClient httpClient,
    IGrowthServiceTokenAcquirer tokenAcquirer,
    ILogger<AuthTenantUserCountClient> logger
) : ITenantUserCountClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<TenantUserCount?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning("Could not acquire a service token to read the user count of {TenantId}.", tenantId);
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"internal/tenants/{tenantId:D}/user-count");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Auth user count for {TenantId} returned {Status}.",
                    tenantId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<TenantUserCountDto>(Json, ct);
            return dto is null ? null : new TenantUserCount(dto.ActiveUsers, dto.PendingInvitations);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Auth user count call threw for tenant {TenantId}.", tenantId);
            return null;
        }
    }

    private sealed record TenantUserCountDto(int ActiveUsers, int PendingInvitations);
}
