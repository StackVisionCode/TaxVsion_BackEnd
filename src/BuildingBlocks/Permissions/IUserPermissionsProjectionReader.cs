namespace BuildingBlocks.Permissions;

/// <summary>
/// Foto local de los permisos efectivos de un usuario en un momento dado — la usa
/// <c>BuildingBlocks.Web.ActorTypeAuthorization.ProjectionPermissionsSource</c> (RBAC Fase 7)
/// para enforzar <c>perm_v</c> sin llamar a Auth por HTTP en el hot path de autorización.
/// </summary>
public sealed record UserPermissionsSnapshot(int PermissionsVersion, IReadOnlyCollection<string> PermissionCodes);

/// <summary>
/// Puerto de solo lectura, deliberadamente angosto (no expone <c>AddAsync</c> ni las consultas de
/// resolución de audiencia que sí necesita <c>IRecipientResolver</c> en Notification) — cada
/// servicio que enforza permisos vía proyección local implementa este puerto ADEMÁS de su propio
/// repositorio de aplicación más rico (mismo patrón que <c>Auth.Infrastructure.AccessTokenDenylist</c>
/// implementando tanto <c>IAccessTokenDenylist</c> como <c>ISessionDenylistReader</c> en RBAC Fase 6 —
/// una sola instancia por scope, sin una segunda lectura redundante del mismo dato).
/// </summary>
public interface IUserPermissionsProjectionReader
{
    Task<UserPermissionsSnapshot?> GetSnapshotAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
