using BuildingBlocks.Permissions;

namespace TaxVision.Campaigns.Infrastructure.Permissions;

/// <summary>
/// Placeholder <b>fail-closed</b> hasta el slice de proyección RBAC (consumers de
/// <c>UserRolesChanged</c>/<c>RolePermissionsChanged</c> que poblarán la proyección local, como en
/// Notes). Devuelve <c>null</c> → sin permisos locales; un <b>PlatformAdmin bypasa</b> en
/// <c>ProjectionPermissionsSource</c>. Necesario para arrancar en modo
/// <c>Authorization:PermissionsSource=Projection</c> sin la maquinaria completa todavía.
/// </summary>
public sealed class EmptyUserPermissionsProjectionReader : IUserPermissionsProjectionReader
{
    public Task<UserPermissionsSnapshot?> GetSnapshotAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult<UserPermissionsSnapshot?>(null);
}
