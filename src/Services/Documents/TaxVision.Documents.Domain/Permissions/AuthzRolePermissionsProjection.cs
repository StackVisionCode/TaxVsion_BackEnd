using System.Text.Json;
using BuildingBlocks.Domain;

namespace TaxVision.Documents.Domain.Permissions;

/// <summary>
/// Cache local de permisos por rol (RBAC Fase 7): permite recomputar la unión de permisos de un usuario
/// multi-rol cuando llega <c>RolePermissionsChangedIntegrationEvent</c> para SOLO uno de sus roles, sin
/// perder los heredados de sus otros roles. El Id es el propio RoleId de Auth (clave natural).
/// </summary>
public sealed class AuthzRolePermissionsProjection : TenantEntity
{
    private AuthzRolePermissionsProjection() { }

    public string RoleName { get; private set; } = string.Empty;
    public string PermissionCodesJson { get; private set; } = "[]";
    public int PermissionsVersion { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

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
