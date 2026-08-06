namespace BuildingBlocks.Permissions;

/// <summary>
/// Opción B (recuperación pull bajo demanda) — snapshot completo de permisos de un usuario tal
/// como lo ve Auth ahora mismo, incluyendo <c>RoleIds</c> (que <see cref="UserPermissionsSnapshot"/>
/// no lleva, porque ese record es solo lo que <c>ProjectionPermissionsSource</c> necesita para
/// autorizar) — un <see cref="IUserPermissionsProjectionWriter"/> necesita los roles para poder
/// persistir la proyección completa, no solo evaluarla.
/// </summary>
public sealed record RemotePermissionsSnapshot(
    int PermissionsVersion,
    IReadOnlyCollection<string> PermissionCodes,
    IReadOnlyCollection<Guid> RoleIds
);

/// <summary>
/// Opción B — puerto M2M hacia Auth para reparar en el momento a un usuario que
/// <see cref="IUserPermissionsProjectionReader"/> todavía no conoce (servicio nuevo que se sumó
/// después de que el backfill global de Auth ya corrió, o cualquier evento perdido). Deliberadamente
/// angosto y sin implementación en BuildingBlocks.Web — cada microservicio que lo necesite trae su
/// propio HttpClient tipado M2M (mismo criterio que <c>IUserPermissionsProjectionReader</c>: el
/// puerto vive acá, la implementación concreta vive en el servicio consumidor). Ningún servicio está
/// obligado a implementarlo — <c>ProjectionPermissionsSource</c> lo trata como opcional y, si no está
/// registrado, se comporta exactamente igual que hoy (fail-closed puro).
/// </summary>
public interface IPermissionsSnapshotClient
{
    /// <summary>Nunca lanza — null en cualquier falla de token/HTTP/404, el caller decide cómo loguear/reintentar (mismo criterio que el resto de los clientes M2M read-only del repo).</summary>
    Task<RemotePermissionsSnapshot?> FetchSnapshotAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
