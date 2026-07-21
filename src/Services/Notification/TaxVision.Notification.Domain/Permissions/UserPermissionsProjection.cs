using System.Text.Json;
using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Permissions;

/// <summary>
/// Proyección local de permisos efectivos por usuario (Fase 4 del plan de notificaciones
/// dinámicas). La usa <c>IRecipientResolver</c> para resolver audiencias <c>ByPermission</c>
/// sin llamar a Auth por HTTP. Mismo espíritu que la proyección homónima de Signature, pero
/// guarda <c>PermissionCodes</c> (no nombres de rol en CSV): acá la resolución es por
/// código de permiso, no por rol.
/// </summary>
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

    public static UserPermissionsProjection Create(
        Guid tenantId,
        Guid userId,
        int permissionsVersion,
        IReadOnlyCollection<string> permissionCodes,
        IReadOnlyCollection<Guid> roleIds
    )
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

    /// <summary>Idempotente por versión monotónica emitida por Auth — eventos fuera de orden se ignoran.</summary>
    public void ApplyIfNewer(
        int permissionsVersion,
        IReadOnlyCollection<string> permissionCodes,
        IReadOnlyCollection<Guid> roleIds
    )
    {
        if (permissionsVersion < PermissionsVersion)
            return;
        PermissionsVersion = permissionsVersion;
        PermissionCodesJson = SerializeCodes(permissionCodes);
        RoleIdsJson = SerializeRoleIds(roleIds);
        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// RolePermissionsChangedIntegrationEvent para UNO de los roles de este usuario: recompone
    /// la unión de permisos de todos sus roles cacheados, sin tocar PermissionsVersion (el
    /// cambio no vino de una reasignación de roles del propio usuario) ni RoleIds.
    /// </summary>
    public void ReapplyPermissionsUnion(IReadOnlyCollection<string> unionOfPermissionCodes)
    {
        PermissionCodesJson = SerializeCodes(unionOfPermissionCodes);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkInactive()
    {
        if (!IsActive)
            return;
        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyList<string> PermissionCodes() => DeserializeCodes(PermissionCodesJson);

    public IReadOnlyList<Guid> RoleIds() => DeserializeRoleIds(RoleIdsJson);

    private static string SerializeCodes(IReadOnlyCollection<string> codes) => JsonSerializer.Serialize(codes);

    private static string SerializeRoleIds(IReadOnlyCollection<Guid> roleIds) => JsonSerializer.Serialize(roleIds);

    private static IReadOnlyList<string> DeserializeCodes(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static IReadOnlyList<Guid> DeserializeRoleIds(string json) =>
        JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
}
