using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Permissions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

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
///
/// <para>
/// <b>Opción B (recuperación pull bajo demanda)</b> — un miss de proyección ya no es
/// automáticamente definitivo: si el servicio registró <paramref name="snapshotClient"/> y
/// <paramref name="projectionWriter"/> (ambos opcionales — parámetros con default <c>null</c>, así
/// que un servicio que nunca los registra en DI se comporta exactamente igual que antes, fail-closed
/// puro), se le pregunta a Auth por el snapshot real y se persiste localmente antes de decidir. Esto
/// resuelve el caso "microservicio nuevo se suma después de que el backfill global de Auth ya corrió
/// para todos los usuarios existentes" sin depender de volver a disparar ese backfill (que
/// re-notificaría a los 10 servicios ya sincronizados, no solo al nuevo) — cada servicio se autorepara
/// en su primer request real, mismo criterio que <c>TenantCustomerBackfillService</c> de Notes.
/// </para>
/// </summary>
public sealed class ProjectionPermissionsSource(
    IUserPermissionsProjectionReader reader,
    IMemoryCache cache,
    ILogger<ProjectionPermissionsSource> logger,
    IPermissionsSnapshotClient? snapshotClient = null,
    IUserPermissionsProjectionWriter? projectionWriter = null
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
                // Size explícito: IMemoryCache es un singleton compartido con el resto del proceso
                // (ej. Scribe también lo usa para su cache de renders con SizeLimit configurado) —
                // cualquier entrada sin Size revienta con InvalidOperationException en cuanto CUALQUIER
                // consumidor del mismo IMemoryCache le puso un SizeLimit, sin importar que esta clase
                // nunca configuró uno. No asumir nada sobre cómo el host configuró la cache compartida.
                entry.Size = 1;
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

                var local = await reader.GetSnapshotAsync(tenantId, userId, ct);
                if (local is not null)
                    return local;

                // Opción B — miss local: si el servicio no registró el mecanismo de recuperación
                // pull, se comporta exactamente igual que antes (null cachea 30s, fail-closed abajo).
                if (snapshotClient is null || projectionWriter is null)
                    return null;

                var remote = await snapshotClient.FetchSnapshotAsync(tenantId, userId, ct);
                if (remote is null)
                    return null;

                await projectionWriter.PersistSnapshotAsync(tenantId, userId, remote, ct);
                logger.LogInformation(
                    "Permissions projection auto-repaired via pull recovery for user {UserId} in tenant {TenantId} (version {Version}).",
                    userId,
                    tenantId,
                    remote.PermissionsVersion
                );
                return new UserPermissionsSnapshot(remote.PermissionsVersion, remote.PermissionCodes);
            }
        );

        if (snapshot is null)
        {
            // Fail-closed: un usuario nunca sincronizado (o cuyo consumer todavía no procesó su
            // primer UserRolesChangedIntegrationEvent) no tiene forma de probar qué permisos
            // tiene realmente — se lo trata como sin acceso, no como "todo permitido". Con Opción B
            // ya registrada, llegar acá significa que la recuperación pull también falló (Auth caído,
            // token M2M no disponible, o el usuario realmente no existe en Auth).
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
