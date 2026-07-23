using TaxVision.Notification.Domain.Authorization;

namespace TaxVision.Notification.Tests.Domain;

/// <summary>
/// Mismo shape/comportamiento que <see cref="TaxVision.Notification.Domain.Permissions.UserPermissionsProjection"/>
/// (ver <c>PermissionsProjectionTests.cs</c>), pero para la proyección de AUTORIZACIÓN (RBAC Fase
/// 7) — entidad distinta, tabla distinta, mismo contrato de negocio (idempotencia por versión,
/// union-recompute multi-rol).
/// </summary>
public sealed class AuthzPermissionsProjectionTests
{
    [Fact]
    public void AuthzUserPermissionsProjection_Create_starts_active_with_given_codes_and_roles()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var projection = AuthzUserPermissionsProjection.Create(
            tenantId,
            userId,
            1,
            ["notification.template.view"],
            [roleId]
        );

        Assert.Equal(tenantId, projection.TenantId);
        Assert.Equal(userId, projection.UserId);
        Assert.True(projection.IsActive);
        Assert.Equal(["notification.template.view"], projection.PermissionCodes());
        Assert.Equal([roleId], projection.RoleIds());
    }

    [Fact]
    public void AuthzUserPermissionsProjection_ApplyIfNewer_ignores_out_of_order_events()
    {
        var projection = AuthzUserPermissionsProjection.Create(Guid.NewGuid(), Guid.NewGuid(), 3, ["a", "b"], []);

        projection.ApplyIfNewer(2, ["stale"], []);

        Assert.Equal(3, projection.PermissionsVersion);
        Assert.Equal(["a", "b"], projection.PermissionCodes());
    }

    [Fact]
    public void AuthzUserPermissionsProjection_ApplyIfNewer_applies_newer_version()
    {
        var roleId = Guid.NewGuid();
        var projection = AuthzUserPermissionsProjection.Create(Guid.NewGuid(), Guid.NewGuid(), 1, ["a"], []);

        projection.ApplyIfNewer(2, ["a", "b"], [roleId]);

        Assert.Equal(2, projection.PermissionsVersion);
        Assert.Equal(["a", "b"], projection.PermissionCodes());
        Assert.Equal([roleId], projection.RoleIds());
    }

    [Fact]
    public void AuthzUserPermissionsProjection_ReapplyPermissionsUnion_does_not_change_version_or_roles()
    {
        var roleId = Guid.NewGuid();
        var projection = AuthzUserPermissionsProjection.Create(Guid.NewGuid(), Guid.NewGuid(), 5, ["a"], [roleId]);

        projection.ReapplyPermissionsUnion(["a", "b", "c"]);

        Assert.Equal(5, projection.PermissionsVersion);
        Assert.Equal([roleId], projection.RoleIds());
        Assert.Equal(["a", "b", "c"], projection.PermissionCodes());
    }

    [Fact]
    public void AuthzRolePermissionsProjection_Create_uses_the_role_id_as_the_entity_id()
    {
        var roleId = Guid.NewGuid();

        var projection = AuthzRolePermissionsProjection.Create(Guid.NewGuid(), roleId, "Admin", 1, ["a"]);

        Assert.Equal(roleId, projection.Id);
        Assert.Equal(["a"], projection.PermissionCodes());
    }

    [Fact]
    public void AuthzRolePermissionsProjection_ApplyIfNewer_ignores_out_of_order_events()
    {
        var projection = AuthzRolePermissionsProjection.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", 4, ["a", "b"]);

        projection.ApplyIfNewer("Admin", 3, ["stale"]);

        Assert.Equal(4, projection.PermissionsVersion);
        Assert.Equal(["a", "b"], projection.PermissionCodes());
    }
}
