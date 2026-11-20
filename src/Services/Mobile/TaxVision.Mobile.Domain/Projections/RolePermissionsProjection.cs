using System.Text.Json;
using BuildingBlocks.Domain;

namespace TaxVision.Mobile.Domain.Projections;

// Se guarda para ver los cambios de un rol; nunca se une a los códigos efectivos de un usuario.
public sealed class RolePermissionsProjection : TenantEntity
{
    private RolePermissionsProjection() { }

    public string RoleName { get; private set; } = string.Empty;
    public int PermissionsVersion { get; private set; }
    public string PermissionCodesJson { get; private set; } = "[]";
    public DateTime UpdatedAtUtc { get; private set; }

    public static RolePermissionsProjection Create(
        Guid tenantId,
        Guid roleId,
        string name,
        int version,
        IReadOnlyCollection<string> codes
    )
    {
        var projection = new RolePermissionsProjection { Id = roleId };
        projection.SetTenant(tenantId);
        projection.ApplyIfNewer(name, version, codes);
        return projection;
    }

    public void ApplyIfNewer(string name, int version, IReadOnlyCollection<string> codes)
    {
        if (version < PermissionsVersion)
            return;
        RoleName = name;
        PermissionsVersion = version;
        PermissionCodesJson = JsonSerializer.Serialize(codes.Distinct(StringComparer.Ordinal).ToArray());
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyList<string> PermissionCodes() =>
        JsonSerializer.Deserialize<List<string>>(PermissionCodesJson) ?? [];
}
