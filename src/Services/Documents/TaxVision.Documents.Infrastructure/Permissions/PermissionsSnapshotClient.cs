using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using Microsoft.Extensions.Logging;

namespace TaxVision.Documents.Infrastructure.Permissions;

/// <summary>
/// Opción B (recuperación pull bajo demanda) — cliente M2M hacia el endpoint interno de Auth
/// (<c>InternalPermissionsSnapshotController</c>). Sin esto, Documents quedaba fail-closed permanente
/// para cualquier usuario cuyo <c>UserRolesChangedIntegrationEvent</c> nunca llegó a este servicio
/// (típicamente porque Documents estaba caído cuando Auth emitió el broadcast, o se sumó como consumidor
/// después del backfill): su tabla de proyección solo la alimentan los eventos de cambio de rol, así que
/// un miss local se volvía 403 definitivo — justo el gap que dejaba <c>GET /documents/branding</c> en 403
/// para un Tenant Admin con el permiso correcto en Auth.
///
/// <para>
/// Reusa el <see cref="IServiceTokenAcquirer"/> que Documents ya tenía (cliente <c>documents-worker</c>,
/// audiencia <c>TaxVision.Services</c>) — la misma que Auth exige en la política <c>ServiceOnly</c> del
/// endpoint interno. No agrega un proveedor de tokens nuevo: el acquirer es de un solo cliente y su
/// audiencia ya sirve para este endpoint (a diferencia de Billing, cuyos otros clientes tienen audiencias
/// acotadas y por eso necesitó un cliente de plataforma aparte).
/// </para>
/// </summary>
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
