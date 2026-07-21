using TaxVision.Notification.Domain.Permissions;

namespace TaxVision.Notification.Tests.Domain;

public sealed class PermissionsProjectionTests
{
    [Fact]
    public void UserPermissionsProjection_Create_starts_active_with_given_codes_and_roles()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var projection = UserPermissionsProjection.Create(tenantId, userId, 1, ["cloudstorage.file.view"], [roleId]);

        Assert.Equal(tenantId, projection.TenantId);
        Assert.Equal(userId, projection.UserId);
        Assert.True(projection.IsActive);
        Assert.Equal(["cloudstorage.file.view"], projection.PermissionCodes());
        Assert.Equal([roleId], projection.RoleIds());
    }

    [Fact]
    public void UserPermissionsProjection_ApplyIfNewer_ignores_out_of_order_events()
    {
        var projection = UserPermissionsProjection.Create(Guid.NewGuid(), Guid.NewGuid(), 3, ["a", "b"], []);

        projection.ApplyIfNewer(2, ["stale"], []);

        Assert.Equal(3, projection.PermissionsVersion);
        Assert.Equal(["a", "b"], projection.PermissionCodes());
    }

    [Fact]
    public void UserPermissionsProjection_ApplyIfNewer_applies_newer_version()
    {
        var roleId = Guid.NewGuid();
        var projection = UserPermissionsProjection.Create(Guid.NewGuid(), Guid.NewGuid(), 1, ["a"], []);

        projection.ApplyIfNewer(2, ["a", "b"], [roleId]);

        Assert.Equal(2, projection.PermissionsVersion);
        Assert.Equal(["a", "b"], projection.PermissionCodes());
        Assert.Equal([roleId], projection.RoleIds());
    }

    [Fact]
    public void UserPermissionsProjection_ReapplyPermissionsUnion_does_not_change_version_or_roles()
    {
        var roleId = Guid.NewGuid();
        var projection = UserPermissionsProjection.Create(Guid.NewGuid(), Guid.NewGuid(), 5, ["a"], [roleId]);

        projection.ReapplyPermissionsUnion(["a", "b", "c"]);

        Assert.Equal(5, projection.PermissionsVersion);
        Assert.Equal([roleId], projection.RoleIds());
        Assert.Equal(["a", "b", "c"], projection.PermissionCodes());
    }

    [Fact]
    public void UserPermissionsProjection_MarkInactive_is_idempotent()
    {
        var projection = UserPermissionsProjection.Create(Guid.NewGuid(), Guid.NewGuid(), 1, [], []);

        projection.MarkInactive();
        var firstUpdate = projection.UpdatedAtUtc;
        projection.MarkInactive();

        Assert.False(projection.IsActive);
        Assert.Equal(firstUpdate, projection.UpdatedAtUtc);
    }

    [Fact]
    public void RolePermissionsProjection_Create_uses_the_role_id_as_the_entity_id()
    {
        var roleId = Guid.NewGuid();

        var projection = RolePermissionsProjection.Create(Guid.NewGuid(), roleId, "Admin", 1, ["a"]);

        Assert.Equal(roleId, projection.Id);
        Assert.Equal(["a"], projection.PermissionCodes());
    }

    [Fact]
    public void RolePermissionsProjection_ApplyIfNewer_ignores_out_of_order_events()
    {
        var projection = RolePermissionsProjection.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", 4, ["a", "b"]);

        projection.ApplyIfNewer("Admin", 3, ["stale"]);

        Assert.Equal(4, projection.PermissionsVersion);
        Assert.Equal(["a", "b"], projection.PermissionCodes());
    }
}
