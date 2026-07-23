using System.Text.Json;
using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Permissions;

/// <summary>
/// Cache local de permisos por rol (RBAC Fase 7, mismo patrón que CloudStorage/Customer/etc.):
/// permite recomputar la unión de permisos de un usuario multi-rol cuando llega
/// <c>RolePermissionsChangedIntegrationEvent</c> para SOLO uno de sus roles, sin perder los
/// permisos heredados de sus otros roles. Ver <see cref="AuthzUserPermissionsProjection"/> para
/// el porqué del prefijo "Authz" (evita colisión con la proyección de auditoría homónima ya
/// existente en este servicio).
/// </summary>
public sealed class AuthzRolePermissionsProjection : TenantEntity
{
    private AuthzRolePermissionsProjection() { }

    public string RoleName { get; private set; } = string.Empty;
    public string PermissionCodesJson { get; private set; } = "[]";
    public int PermissionsVersion { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>El Id es el propio RoleId de Auth — clave natural, un rol tiene una única fila.</summary>
    public static AuthzRolePermissionsProjection Create(
        Guid tenantId,
        Guid roleId,
        string roleName,
        int permissionsVersion,
        IReadOnlyCollection<string> permissionCodes
    )
    {
        var projection = new AuthzRolePermissionsProjection
        {
            Id = roleId,
            RoleName = roleName,
            PermissionCodesJson = Serialize(permissionCodes),
            PermissionsVersion = permissionsVersion,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void ApplyIfNewer(string roleName, int permissionsVersion, IReadOnlyCollection<string> permissionCodes)
    {
        if (permissionsVersion < PermissionsVersion)
            return;
        RoleName = roleName;
        PermissionsVersion = permissionsVersion;
        PermissionCodesJson = Serialize(permissionCodes);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyList<string> PermissionCodes() =>
        JsonSerializer.Deserialize<List<string>>(PermissionCodesJson) ?? [];

    private static string Serialize(IReadOnlyCollection<string> codes) => JsonSerializer.Serialize(codes);
}
