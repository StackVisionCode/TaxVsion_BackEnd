using System.Security.Claims;
using BuildingBlocks.Permissions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Enforza <c>perm_v</c> contra la proyección local (RBAC Fase 7) — si el JWT trae una versión
/// menor a la de la proyección, el usuario cambió de permisos desde que se emitió ese token y el
/// pedido se rechaza con <c>Auth.TokenStale</c> (ver <see cref="UnauthorizedAccessException"/>,
/// mapeado a 401 en <c>ExceptionHandlingMiddleware</c>) — el frontend refresca y obtiene un JWT
/// con <c>perm_v</c> al día. Cache in-memory de 30s: sin esto, cada request autorizado pega contra
/// la base de proyecciones, agregando latencia al hot path de todo endpoint con
/// <c>[HasPermission]</c>.
///
/// <para>
/// <b>RBAC Fase 7.5</b> — los tokens M2M (<c>GenerateScopedServiceToken</c>) nunca llevan
/// <c>perm_v</c> ni tienen fila de proyección propia (su <c>sub</c> es un GUID sintético derivado
/// del <c>clientId</c>, nunca sincronizado por <c>UserRolesChangedIntegrationEvent</c>) — sin este
/// bypass, cualquier endpoint M2M que combine <c>[AllowActorTypes(ActorType.Service)]</c> con
/// <c>[HasPermission]</c> (ej. <c>POST scribe/render</c>, llamado por Notification) fallaría cerrado
/// siempre en modo <c>"Projection"</c>. Los permisos de un client de servicio son estáticos por
/// registro en Auth, no cambian dinámicamente como los de un usuario humano — no necesitan
/// staleness-checking vía <c>perm_v</c>, así que leerlos directo del claim <c>perm</c> del token
/// (mismo comportamiento que <see cref="JwtEmbeddedPermissionsSource"/>) es seguro y suficiente.
/// </para>
/// </summary>
public sealed class ProjectionPermissionsSource(
    IUserPermissionsProjectionReader reader,
    IMemoryCache cache,
    ILogger<ProjectionPermissionsSource> logger
) : IUserPermissionsSource
{
    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default)
    {
        if (user.IsPlatformAdmin())
            return true;

        if (user.GetActorType() == ActorType.Service)
            return user.HasPermission(permission);

        if (!user.TryGetUserId(out var userId) || !user.TryGetTenantId(out var tenantId))
            return false;

        var jwtPermissionsVersion = user.GetPermissionsVersion();
        var snapshot = await cache.GetOrCreateAsync(
            $"perm-proj:{tenantId:N}:{userId:N}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                return await reader.GetSnapshotAsync(tenantId, userId, ct);
            }
        );

        if (snapshot is null)
        {
            // Fail-closed: un usuario nunca sincronizado (o cuyo consumer todavía no procesó su
            // primer UserRolesChangedIntegrationEvent) no tiene forma de probar qué permisos
            // tiene realmente — se lo trata como sin acceso, no como "todo permitido".
            logger.LogWarning(
                "No UserPermissionsProjection found for user {UserId} in tenant {TenantId} — failing closed.",
                userId,
                tenantId
            );
            return false;
        }

        if (jwtPermissionsVersion < snapshot.PermissionsVersion)
            throw new UnauthorizedAccessException("Auth.TokenStale");

        return snapshot.PermissionCodes.Contains(permission, StringComparer.Ordinal);
    }
}
