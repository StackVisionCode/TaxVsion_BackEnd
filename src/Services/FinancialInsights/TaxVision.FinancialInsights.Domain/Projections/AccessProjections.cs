using System.Text.Json;
using BuildingBlocks.Domain;

namespace TaxVision.FinancialInsights.Domain.Projections;

// Auth publishes effective permission codes (roles minus per-user denies).
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

    public static UserPermissionsProjection Create(Guid tenantId, Guid userId, int version,
        IReadOnlyCollection<string> codes, IReadOnlyCollection<Guid> roleIds)
    {
        var projection = new UserPermissionsProjection { Id = Guid.NewGuid(), UserId = userId, IsActive = true };
        projection.SetTenant(tenantId);
        projection.ApplyIfNewer(version, codes, roleIds);
        return projection;
    }

    public static UserPermissionsProjection CreateInactive(Guid tenantId, Guid userId, DateTime observedAtUtc)
    {
        var projection = new UserPermissionsProjection { Id = Guid.NewGuid(), UserId = userId,
            LifecycleUpdatedAtUtc = observedAtUtc, UpdatedAtUtc = observedAtUtc };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void ApplyIfNewer(int version, IReadOnlyCollection<string> codes, IReadOnlyCollection<Guid> roleIds)
    {
        if (version < PermissionsVersion) return;
        PermissionsVersion = version;
        PermissionCodesJson = JsonSerializer.Serialize(codes.Distinct(StringComparer.Ordinal).ToArray());
        RoleIdsJson = JsonSerializer.Serialize(roleIds.Distinct().ToArray());
        HasPermissionsSnapshot = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetActive(bool active, DateTime observedAtUtc)
    {
        if (observedAtUtc < LifecycleUpdatedAtUtc) return;
        IsActive = active;
        LifecycleUpdatedAtUtc = observedAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyList<string> PermissionCodes() => JsonSerializer.Deserialize<List<string>>(PermissionCodesJson) ?? [];
}

// Kept for role-change visibility; never unioned into a user's effective codes.
public sealed class RolePermissionsProjection : TenantEntity
{
    private RolePermissionsProjection() { }
    public string RoleName { get; private set; } = string.Empty;
    public int PermissionsVersion { get; private set; }
    public string PermissionCodesJson { get; private set; } = "[]";
    public DateTime UpdatedAtUtc { get; private set; }

    public static RolePermissionsProjection Create(Guid tenantId, Guid roleId, string name, int version,
        IReadOnlyCollection<string> codes)
    {
        var projection = new RolePermissionsProjection { Id = roleId };
        projection.SetTenant(tenantId);
        projection.ApplyIfNewer(name, version, codes);
        return projection;
    }

    public void ApplyIfNewer(string name, int version, IReadOnlyCollection<string> codes)
    {
        if (version < PermissionsVersion) return;
        RoleName = name;
        PermissionsVersion = version;
        PermissionCodesJson = JsonSerializer.Serialize(codes.Distinct(StringComparer.Ordinal).ToArray());
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class TenantEntitlementsProjection : TenantEntity
{
    private TenantEntitlementsProjection() { }
    public string PlanCode { get; private set; } = string.Empty;
    public long RevisionNumber { get; private set; }
    public string EnabledModulesJson { get; private set; } = "[]";
    // Tope mensual de documentos con IA del plan (financial.documents_per_month); null si el plan no lo trae.
    public int? DocumentsPerMonth { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyList<string> EnabledModules => JsonSerializer.Deserialize<List<string>>(EnabledModulesJson) ?? [];

    public static TenantEntitlementsProjection Create(Guid tenantId, string planCode, long revision,
        IReadOnlyList<string> modules, int? documentsPerMonth)
    {
        var projection = new TenantEntitlementsProjection { Id = tenantId };
        projection.SetTenant(tenantId);
        projection.ApplyIfNewer(planCode, revision, modules, documentsPerMonth);
        return projection;
    }

    public void ApplyIfNewer(string planCode, long revision, IReadOnlyList<string> modules, int? documentsPerMonth)
    {
        if (revision < RevisionNumber) return;
        PlanCode = planCode;
        RevisionNumber = revision;
        EnabledModulesJson = JsonSerializer.Serialize(modules);
        DocumentsPerMonth = documentsPerMonth is >= 0 ? documentsPerMonth : null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public bool HasModule(string module) => EnabledModules.Contains(module, StringComparer.OrdinalIgnoreCase);

    // El plan manda; la configuración solo cubre planes que aún no envían la clave.
    public int MonthlyDocumentLimit(int fallback) => DocumentsPerMonth ?? fallback;
}

public sealed class TenantDirectoryProjection : TenantEntity
{
    private TenantDirectoryProjection() { }
    public string Name { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public long? TenantVersion { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantDirectoryProjection Create(Guid tenantId, string name, DateTime observedAtUtc)
    {
        var projection = new TenantDirectoryProjection { Id = tenantId, Name = name, Status = "Active", IsActive = true, UpdatedAtUtc = observedAtUtc };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void ApplyStatus(string status, bool active, long? version, DateTime observedAtUtc)
    {
        if (version.HasValue && TenantVersion.HasValue && version < TenantVersion) return;
        if (!version.HasValue && observedAtUtc < UpdatedAtUtc) return;
        Status = status;
        IsActive = active;
        TenantVersion = version ?? TenantVersion;
        UpdatedAtUtc = observedAtUtc;
    }

    public void ApplyName(string name, long? version, DateTime observedAtUtc)
    {
        if (version.HasValue && TenantVersion.HasValue && version < TenantVersion) return;
        if (!version.HasValue && observedAtUtc < UpdatedAtUtc) return;
        Name = name;
        TenantVersion = version ?? TenantVersion;
        UpdatedAtUtc = observedAtUtc;
    }
}

public sealed class CustomerDirectoryProjection : TenantEntity
{
    private CustomerDirectoryProjection() { }
    public Guid CustomerId { get; private set; }
    public string? DisplayName { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static CustomerDirectoryProjection Create(Guid tenantId, Guid customerId, string? name, DateTime observedAtUtc)
    {
        var projection = new CustomerDirectoryProjection { Id = Guid.NewGuid(), CustomerId = customerId,
            DisplayName = name, IsActive = true, UpdatedAtUtc = observedAtUtc };
        projection.SetTenant(tenantId);
        return projection;
    }

    public void Apply(string? name, bool active, DateTime observedAtUtc)
    {
        if (observedAtUtc < UpdatedAtUtc) return;
        if (name is not null) DisplayName = name;
        IsActive = active;
        UpdatedAtUtc = observedAtUtc;
    }
}
