using System.Text.Json;
using BuildingBlocks.Domain;

namespace TaxVision.Inventory.Domain.Permissions;

/// <summary>Proyección local de permisos efectivos por usuario (RBAC Fase 7). Mismo shape que Sms/Catalog.</summary>
public sealed class UserPermissionsProjection : TenantEntity
{
    private UserPermissionsProjection() { }

    public Guid UserId { get; private set; }
    public int PermissionsVersion { get; private set; }
    public string PermissionCodesJson { get; private set; } = "[]";
    public string RoleIdsJson { get; private set; } = "[]";
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static UserPermissionsProjection Create(Guid tenantId, Guid userId, int permissionsVersion, IReadOnlyCollection<string> permissionCodes, IReadOnlyCollection<Guid> roleIds)
    {
        var now = DateTime.UtcNow;
        var projection = new UserPermissionsProjection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionsVersion = permissionsVersion,
            PermissionCodesJson = SerializeCodes(permissionCodes),
            RoleIdsJson = SerializeRoleIds(roleIds),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void ApplyIfNewer(int permissionsVersion, IReadOnlyCollection<string> permissionCodes, IReadOnlyCollection<Guid> roleIds)
    {
        if (permissionsVersion < PermissionsVersion)
            return;
        PermissionsVersion = permissionsVersion;
        PermissionCodesJson = SerializeCodes(permissionCodes);
        RoleIdsJson = SerializeRoleIds(roleIds);
        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ReapplyPermissionsUnion(IReadOnlyCollection<string> unionOfPermissionCodes)
    {
        PermissionCodesJson = SerializeCodes(unionOfPermissionCodes);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyList<string> PermissionCodes() => DeserializeCodes(PermissionCodesJson);

    public IReadOnlyList<Guid> RoleIds() => DeserializeRoleIds(RoleIdsJson);

    private static string SerializeCodes(IReadOnlyCollection<string> codes) => JsonSerializer.Serialize(codes);

    private static string SerializeRoleIds(IReadOnlyCollection<Guid> roleIds) => JsonSerializer.Serialize(roleIds);

    private static IReadOnlyList<string> DeserializeCodes(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static IReadOnlyList<Guid> DeserializeRoleIds(string json) => JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
}
