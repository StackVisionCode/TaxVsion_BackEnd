using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using Microsoft.Extensions.Configuration;

namespace TaxVision.FinancialInsights.Infrastructure.Permissions;

public sealed class FinancialPermissionsSnapshotClient(HttpClient http, IConfiguration configuration)
    : IPermissionsSnapshotClient
{
    public async Task<RemotePermissionsSnapshot?> FetchSnapshotAsync(Guid tenantId, Guid userId,
        CancellationToken ct = default)
    {
        var clientId = configuration["ServiceAuthClient:ClientId"];
        var secret = configuration["ServiceAuthClient:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret) || http.BaseAddress is null)
            return null;
        try
        {
            var grant = await http.RequestServiceTokenAsync(clientId, secret, tenantId, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"internal/tenants/{tenantId:D}/users/{userId:D}/permissions-snapshot");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", grant.AccessToken);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var dto = await response.Content.ReadFromJsonAsync<SnapshotDto>(cancellationToken: ct);
            return dto is null ? null : new RemotePermissionsSnapshot(dto.PermissionsVersion,
                dto.PermissionCodes, dto.RoleIds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
            or ServiceTokenAcquisitionException)
        { return null; }
    }

    private sealed record SnapshotDto(int PermissionsVersion, IReadOnlyList<string> PermissionCodes,
        IReadOnlyList<Guid> RoleIds);
}
