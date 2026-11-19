using System.Text.Json;
using BuildingBlocks.Domain;

namespace TaxVision.Mobile.Domain.Projections;

// Auth entrega permisos efectivos; nunca se recompone la union de roles.
public sealed class UserPermissionsProjection : TenantEntity
{
    private UserPermissionsProjection() { }

    [UserReference(UserReferenceKind.Projection, HandledBy = "UserLifecyclePermissionsProjectionConsumer")]
    public Guid UserId { get; private set; }
    public int PermissionsVersion { get; private set; }
    public string PermissionCodesJson { get; private set; } = "[]";
    public string RoleIdsJson { get; private set; } = "[]";
    public bool IsActive { get; private set; }
    public bool HasPermissionsSnapshot { get; private set; }
    public DateTime LifecycleUpdatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static UserPermissionsProjection Create(
        Guid tenantId,
        Guid userId,
        int version,
        IReadOnlyCollection<string> codes,
        IReadOnlyCollection<Guid> roleIds,
        DateTime observedAtUtc
    )
    {
        var projection = new UserPermissionsProjection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IsActive = true,
        };
        projection.SetTenant(tenantId);
        projection.ApplyIfNewer(version, codes, roleIds, observedAtUtc);
        return projection;
    }

    public static UserPermissionsProjection CreateInactive(Guid tenantId, Guid userId, DateTime observedAtUtc)
    {
        var projection = new UserPermissionsProjection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LifecycleUpdatedAtUtc = observedAtUtc,
            UpdatedAtUtc = observedAtUtc,
        };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void ApplyIfNewer(
        int version,
        IReadOnlyCollection<string> codes,
        IReadOnlyCollection<Guid> roleIds,
        DateTime observedAtUtc
    )
    {
        if (version < PermissionsVersion)
            return;
        PermissionsVersion = version;
        PermissionCodesJson = JsonSerializer.Serialize(new HashSet<string>(codes, StringComparer.Ordinal));
        RoleIdsJson = JsonSerializer.Serialize(new HashSet<Guid>(roleIds));
        HasPermissionsSnapshot = true;
        UpdatedAtUtc = observedAtUtc;
    }

    public void SetActive(bool active, DateTime observedAtUtc)
    {
        if (observedAtUtc < LifecycleUpdatedAtUtc)
            return;
        IsActive = active;
        LifecycleUpdatedAtUtc = observedAtUtc;
        UpdatedAtUtc = observedAtUtc;
    }

    public IReadOnlyList<string> PermissionCodes() =>
        JsonSerializer.Deserialize<List<string>>(PermissionCodesJson) ?? [];
}
