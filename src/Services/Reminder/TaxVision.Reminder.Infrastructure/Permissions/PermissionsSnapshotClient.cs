using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Permissions;
using Microsoft.Extensions.Logging;

namespace TaxVision.Reminder.Infrastructure.Permissions;

/// <summary>
/// Recuperación pull bajo demanda (diferida desde Fase 3, llega acá porque necesita el acquirer
/// M2M): cuando <c>ProjectionPermissionsSource</c> no encuentra fila local para un usuario — evento
/// perdido, usuario nuevo, servicio recién desplegado — le pide el snapshot al endpoint interno de
/// Auth en vez de responder 403. Reutiliza el MISMO <see cref="IServiceTokenAcquirer"/> del catálogo
/// de rate limits: ya apunta a Auth, un acquirer por servicio y un HttpClient tipado por destino.
///
/// <para>
/// Nunca lanza: devuelve null ante cualquier fallo de token/HTTP/404 y el caller decide
/// (fail-closed). Un usuario sin permisos reales y un Auth caído tienen que verse igual desde acá.
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
