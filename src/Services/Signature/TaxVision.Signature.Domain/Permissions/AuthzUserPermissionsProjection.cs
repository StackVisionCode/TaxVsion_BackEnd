using System.Text.Json;
using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Permissions;

/// <summary>
/// Proyección local de permisos efectivos por usuario (RBAC Fase 7 — enforcement de <c>perm_v</c>).
/// La consulta <c>ProjectionPermissionsSource</c> (BuildingBlocks.Web) vía el adaptador de
/// Infrastructure que implementa <c>BuildingBlocks.Permissions.IUserPermissionsProjectionReader</c>.
///
/// <para>
/// <b>Nombre "Authz" deliberado</b>: Signature ya tiene una entidad homónima
/// <see cref="TaxVision.Signature.Domain.Projections.UserPermissionsProjection"/> que sirve un
/// propósito totalmente distinto (snapshot de auditoría — "quién firmó/canceló" en
/// <c>SignatureAuditEvent</c>, solo guarda <c>RolesCsv</c>, no códigos de permiso). Esta clase es
/// la proyección de AUTORIZACIÓN (Fase 7) — misma forma que la homónima de
/// CloudStorage/Customer/Postmaster/etc., pero con un nombre distinto para no colisionar con la
/// tabla/entidad de auditoría ya existente y en uso. <c>RoleIds</c> permite recomputar la unión de
/// permisos de un usuario multi-rol cuando llega <c>RolePermissionsChangedIntegrationEvent</c>
/// para SOLO uno de sus roles, sin perder los heredados de sus otros roles.
/// </para>
/// </summary>
public sealed class AuthzUserPermissionsProjection : TenantEntity
{
    private AuthzUserPermissionsProjection() { }

    public Guid UserId { get; private set; }
    public int PermissionsVersion { get; private set; }
    public string PermissionCodesJson { get; private set; } = "[]";
    public string RoleIdsJson { get; private set; } = "[]";
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static AuthzUserPermissionsProjection Create(
        Guid tenantId,
        Guid userId,
        int permissionsVersion,
        IReadOnlyCollection<string> permissionCodes,
        IReadOnlyCollection<Guid> roleIds
    )
    {
        var now = DateTime.UtcNow;
        var projection = new AuthzUserPermissionsProjection
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

    public IReadOnlyList<string> PermissionCodes() => DeserializeCodes(PermissionCodesJson);

    public IReadOnlyList<Guid> RoleIds() => DeserializeRoleIds(RoleIdsJson);

    private static string SerializeCodes(IReadOnlyCollection<string> codes) => JsonSerializer.Serialize(codes);

    private static string SerializeRoleIds(IReadOnlyCollection<Guid> roleIds) => JsonSerializer.Serialize(roleIds);

    private static IReadOnlyList<string> DeserializeCodes(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static IReadOnlyList<Guid> DeserializeRoleIds(string json) =>
        JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
}
