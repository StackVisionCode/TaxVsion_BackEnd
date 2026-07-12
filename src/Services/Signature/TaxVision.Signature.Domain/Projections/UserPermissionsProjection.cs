using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Projections;

/// <summary>
/// Proyección local de roles/permisos de usuarios del tenant. Se alimenta de
/// <c>UserRolesChangedIntegrationEvent</c> emitido por Auth. La usa Signature para:
/// <list type="bullet">
///   <item>Snapshot del usuario en <c>SignatureAuditEvent</c> (quién firmó/canceló) sin
///     tener que llamar a Auth por HTTP.</item>
///   <item>Decisiones futuras (visibilidad de dashboards, filtros por rol) sin
///     acoplamiento síncrono a Auth.</item>
/// </list>
///
/// <para>
/// La <c>PermissionsVersion</c> es un contador monotónico emitido por Auth: si llegan
/// eventos out-of-order, se conserva el más reciente.
/// </para>
/// </summary>
public sealed class UserPermissionsProjection : TenantEntity
{
    public const int MaxRoleNameLength = 60;
    public const int MaxRolesJoinedLength = 500;

    private UserPermissionsProjection() { }

    public Guid UserId { get; private set; }
    public int PermissionsVersion { get; private set; }

    /// <summary>Roles serializados como CSV para simplicidad — no hay queries sofisticadas sobre este campo.</summary>
    public string RolesCsv { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static UserPermissionsProjection ForNewUser(Guid tenantId, Guid userId, int version, string[] roles)
    {
        var now = DateTime.UtcNow;
        var projection = new UserPermissionsProjection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PermissionsVersion = version,
            RolesCsv = SerializeRoles(roles),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        projection.SetTenant(tenantId);
        return projection;
    }

    /// <summary>Aplica una nueva versión si es más nueva. Idempotente si ya se aplicó.</summary>
    public void ApplyIfNewer(int version, string[] roles)
    {
        if (version <= PermissionsVersion)
            return;
        PermissionsVersion = version;
        RolesCsv = SerializeRoles(roles);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string SerializeRoles(string[] roles)
    {
        if (roles.Length == 0)
            return string.Empty;
        var joined = string.Join(
            ",",
            roles.Select(r => r.Trim()).Where(r => r.Length > 0 && r.Length <= MaxRoleNameLength)
        );
        return joined.Length > MaxRolesJoinedLength ? joined[..MaxRolesJoinedLength] : joined;
    }
}
