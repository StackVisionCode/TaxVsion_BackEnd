namespace TaxVision.Auth.Domain.Roles;

/// <summary>Unión N:M rol–permiso. Clave compuesta (RoleId, PermissionId).</summary>
public sealed class RolePermission
{
    private RolePermission() { }

    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    public static RolePermission Create(Guid roleId, Guid permissionId) =>
        new() { RoleId = roleId, PermissionId = permissionId };
}
