using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using Microsoft.Extensions.Logging;

namespace TaxVision.Sms.Infrastructure.Permissions;

// ---------------------------------------------------------------------------
// Opción B (recuperación pull bajo demanda) — cliente M2M hacia el endpoint interno de Auth
// (GetPermissionsSnapshotHandler). Reutiliza el MISMO IServiceTokenAcquirer del rate-limit (ya apunta
// a Auth). Sin esto, un usuario sin fila local de proyección (nunca sincronizado por Sms) recibía 403
// fail-closed permanente hasta la reconciliación de 6h. Nunca lanza — null en cualquier falla de
// token/HTTP/404; el caller (ProjectionPermissionsSource) decide (fail-closed si esto devuelve null).
// ---------------------------------------------------------------------------

internal sealed class PermissionsSnapshotClient(
    HttpClient httpClient,
    IServiceTokenAcquirer tokenAcquirer,
    ILogger<PermissionsSnapshotClient> logger
) : IPermissionsSnapshotClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<RemotePermissionsSnapshot?> FetchSnapshotAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/tenants/{tenantId:D}/users/{userId:D}/permissions-snapshot"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Permissions snapshot pull for user {UserId} in tenant {TenantId} returned {Status}.",
                    userId,
                    tenantId,
                    (int)response.StatusCode
                );
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<SnapshotDto>(Json, ct);
            return dto is null
                ? null
                : new RemotePermissionsSnapshot(dto.PermissionsVersion, dto.PermissionCodes, dto.RoleIds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Permissions snapshot pull call threw.");
            return null;
        }
    }

    private sealed record SnapshotDto(
        int PermissionsVersion,
        IReadOnlyList<string> PermissionCodes,
        IReadOnlyList<Guid> RoleIds
    );
}
