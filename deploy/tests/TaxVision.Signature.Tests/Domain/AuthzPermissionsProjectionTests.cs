using TaxVision.Signature.Domain.Permissions;

namespace TaxVision.Signature.Tests.Domain;

// RBAC Fase 7 -- cierra el gap real de Signature: registraba PermissionPolicyProvider (y
// [HasPermission] en 40+ acciones) pero nunca registraba ningun IUserPermissionsSource, asi que
// todo endpoint decorado tiraba InvalidOperationException al resolver la dependencia. Estas
// pruebas cubren las dos entidades de la proyeccion de AUTORIZACION (distinta de la proyeccion
// de auditoria homonima UserPermissionsProjection -- ver ProjectionTests.cs / docblocks).
public sealed class AuthzPermissionsProjectionTests
{
    // -------------------- AuthzUserPermissionsProjection --------------------

    [Fact]
    public void Create_stores_permission_codes_and_role_ids_and_is_active()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var projection = AuthzUserPermissionsProjection.Create(
            tenantId,
            userId,
            permissionsVersion: 1,
            permissionCodes: ["customers.view", "customers.edit"],
            roleIds: [roleId]
        );

        Assert.Equal(userId, projection.UserId);
        Assert.Equal(tenantId, projection.TenantId);
        Assert.Equal(1, projection.PermissionsVersion);
        Assert.True(projection.IsActive);
        Assert.Equal(["customers.view", "customers.edit"], projection.PermissionCodes());
        Assert.Equal([roleId], projection.RoleIds());
    }

    [Fact]
    public void ApplyIfNewer_updates_when_version_is_newer()
    {
        var projection = AuthzUserPermissionsProjection.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            permissionsVersion: 1,
            permissionCodes: ["a"],
            roleIds: []
        );

        projection.ApplyIfNewer(2, ["a", "b"], [Guid.NewGuid()]);

        Assert.Equal(2, projection.PermissionsVersion);
        Assert.Equal(["a", "b"], projection.PermissionCodes());
    }

    [Fact]
    public void ApplyIfNewer_ignores_out_of_order_events_with_older_version()
    {
        var projection = AuthzUserPermissionsProjection.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            permissionsVersion: 5,
            permissionCodes: ["a", "b"],
            roleIds: []
        );

        projection.ApplyIfNewer(3, ["stale"], []);

        Assert.Equal(5, projection.PermissionsVersion);
        Assert.Equal(["a", "b"], projection.PermissionCodes());
    }

    [Fact]
    public void ReapplyPermissionsUnion_replaces_codes_without_touching_version_or_role_ids()
    {
        var roleId = Guid.NewGuid();
        var projection = AuthzUserPermissionsProjection.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            permissionsVersion: 4,
            permissionCodes: ["a"],
            roleIds: [roleId]
        );

        projection.ReapplyPermissionsUnion(["a", "b", "c"]);

        Assert.Equal(4, projection.PermissionsVersion);
        Assert.Equal([roleId], projection.RoleIds());
        Assert.Equal(new[] { "a", "b", "c" }, projection.PermissionCodes().OrderBy(c => c));
    }

    // -------------------- AuthzRolePermissionsProjection --------------------

    [Fact]
    public void Create_uses_role_id_as_the_entity_id()
    {
        var roleId = Guid.NewGuid();

        var projection = AuthzRolePermissionsProjection.Create(
            Guid.NewGuid(),
            roleId,
            "TenantAdmin",
            permissionsVersion: 1,
            permissionCodes: ["customers.view"]
        );

        Assert.Equal(roleId, projection.Id);
        Assert.Equal("TenantAdmin", projection.RoleName);
        Assert.Equal(["customers.view"], projection.PermissionCodes());
    }

    [Fact]
    public void ApplyIfNewer_on_role_updates_name_and_codes_when_newer()
    {
        var projection = AuthzRolePermissionsProjection.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Old",
            permissionsVersion: 1,
            permissionCodes: ["a"]
        );

        projection.ApplyIfNewer("New", 2, ["a", "b"]);

        Assert.Equal("New", projection.RoleName);
        Assert.Equal(2, projection.PermissionsVersion);
        Assert.Equal(["a", "b"], projection.PermissionCodes());
    }

    [Fact]
    public void ApplyIfNewer_on_role_ignores_out_of_order_events()
    {
        var projection = AuthzRolePermissionsProjection.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Current",
            permissionsVersion: 3,
            permissionCodes: ["a"]
        );

        projection.ApplyIfNewer("Stale", 1, ["stale"]);

        Assert.Equal("Current", projection.RoleName);
        Assert.Equal(3, projection.PermissionsVersion);
        Assert.Equal(["a"], projection.PermissionCodes());
    }
}
