using TaxVision.Sms.Domain.Permissions;

namespace TaxVision.Sms.Tests.Domain;

public sealed class PermissionsProjectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid RoleA = Guid.NewGuid();

    [Fact]
    public void UserProjection_Create_exposes_codes_and_roles()
    {
        var p = UserPermissionsProjection.Create(Tenant, User, 3, ["sms.send"], [RoleA]);

        Assert.Equal(3, p.PermissionsVersion);
        Assert.Contains("sms.send", p.PermissionCodes());
        Assert.Contains(RoleA, p.RoleIds());
        Assert.Equal(Tenant, p.TenantId);
    }

    [Fact]
    public void UserProjection_ApplyIfNewer_applies_a_higher_version()
    {
        var p = UserPermissionsProjection.Create(Tenant, User, 1, ["old"], [RoleA]);

        p.ApplyIfNewer(2, ["sms.send"], [RoleA]);

        Assert.Equal(2, p.PermissionsVersion);
        Assert.Contains("sms.send", p.PermissionCodes());
        Assert.DoesNotContain("old", p.PermissionCodes());
    }

    [Fact]
    public void UserProjection_ApplyIfNewer_ignores_an_older_version()
    {
        var p = UserPermissionsProjection.Create(Tenant, User, 5, ["sms.send"], [RoleA]);

        p.ApplyIfNewer(4, ["stale"], [RoleA]);

        Assert.Equal(5, p.PermissionsVersion);
        Assert.Contains("sms.send", p.PermissionCodes());
        Assert.DoesNotContain("stale", p.PermissionCodes());
    }

    [Fact]
    public void UserProjection_ReapplyPermissionsUnion_replaces_codes_without_touching_version()
    {
        var p = UserPermissionsProjection.Create(Tenant, User, 7, ["a"], [RoleA]);

        p.ReapplyPermissionsUnion(["sms.send", "a"]);

        Assert.Equal(7, p.PermissionsVersion); // unchanged
        Assert.Contains("sms.send", p.PermissionCodes());
        Assert.Contains("a", p.PermissionCodes());
    }

    [Fact]
    public void RoleProjection_ApplyIfNewer_respects_version_ordering()
    {
        var role = RolePermissionsProjection.Create(Tenant, RoleA, "Tenant Admin", 1, ["a"]);

        role.ApplyIfNewer("Tenant Admin", 2, ["sms.send"]);
        Assert.Equal(2, role.PermissionsVersion);
        Assert.Contains("sms.send", role.PermissionCodes());

        role.ApplyIfNewer("Tenant Admin", 1, ["stale"]); // older ignored
        Assert.Equal(2, role.PermissionsVersion);
        Assert.Contains("sms.send", role.PermissionCodes());
    }
}
