using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;

namespace TaxVision.Mobile.Infrastructure.Permissions;

public sealed class MobilePermissionsSnapshotClient(HttpClient http, IServiceTokenAcquirer tokens)
    : IPermissionsSnapshotClient
{
    public async Task<RemotePermissionsSnapshot?> FetchSnapshotAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        if (http.BaseAddress is null)
            return null;
        try
        {
            var token = await tokens.GetTokenAsync(tenantId, ct);
            if (token is null)
                return null;
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/tenants/{tenantId:D}/users/{userId:D}/permissions-snapshot"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            var dto = await response.Content.ReadFromJsonAsync<SnapshotDto>(cancellationToken: ct);
            return dto is null
                ? null
                : new RemotePermissionsSnapshot(dto.PermissionsVersion, dto.PermissionCodes, dto.RoleIds);
        }
        catch (Exception ex)
            when (!ct.IsCancellationRequested
                && ex
                    is HttpRequestException
                        or TaskCanceledException
                        or System.Text.Json.JsonException
                        or ServiceTokenAcquisitionException
            )
        {
            return null;
        }
    }

    private sealed record SnapshotDto(
        int PermissionsVersion,
        IReadOnlyList<string> PermissionCodes,
        IReadOnlyList<Guid> RoleIds
    );
}
