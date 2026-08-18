using TaxVision.Catalog.Domain;
using TaxVision.Catalog.Domain.Categories;
using TaxVision.Catalog.Domain.Permissions;

namespace TaxVision.Catalog.Tests.Domain;

public sealed class CategoryTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Create_starts_active_and_snapshots()
    {
        var c = Category.Create(Tenant, Guid.NewGuid(), "Services", "desc", null, Now).Value;
        Assert.Equal("Services", c.Name);
        Assert.True(c.IsActive);
        Assert.Null(c.ParentCategoryId);
        Assert.Equal(Tenant, c.TenantId);
    }

    [Fact]
    public void Create_rejects_blank_name()
    {
        var r = Category.Create(Tenant, Guid.NewGuid(), "  ", null, null, Now);
        Assert.True(r.IsFailure);
        Assert.Equal(CatalogErrors.InvalidName.Code, r.Error.Code);
    }

    [Fact]
    public void Update_rejects_self_parent_cycle()
    {
        var c = Category.Create(Tenant, Guid.NewGuid(), "A", null, null, Now).Value;
        var r = c.Update("A", null, c.Id, Now);
        Assert.True(r.IsFailure);
        Assert.Equal("catalog.categoryCycle", r.Error.Code);
    }

    [Fact]
    public void SoftDelete_marks_deleted()
    {
        var c = Category.Create(Tenant, Guid.NewGuid(), "A", null, null, Now).Value;
        c.SoftDelete(Now);
        Assert.True(c.IsDeleted);
    }
}

public sealed class PermissionsProjectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid RoleA = Guid.NewGuid();

    [Fact]
    public void UserProjection_ApplyIfNewer_applies_higher_ignores_older()
    {
        var p = UserPermissionsProjection.Create(Tenant, User, 1, ["old"], [RoleA]);
        p.ApplyIfNewer(2, ["catalog.read"], [RoleA]);
        Assert.Equal(2, p.PermissionsVersion);
        Assert.Contains("catalog.read", p.PermissionCodes());

        p.ApplyIfNewer(1, ["stale"], [RoleA]); // ignored
        Assert.Contains("catalog.read", p.PermissionCodes());
        Assert.DoesNotContain("stale", p.PermissionCodes());
    }

    [Fact]
    public void UserProjection_ReapplyPermissionsUnion_keeps_version()
    {
        var p = UserPermissionsProjection.Create(Tenant, User, 7, ["a"], [RoleA]);
        p.ReapplyPermissionsUnion(["catalog.read", "catalog.write"]);
        Assert.Equal(7, p.PermissionsVersion);
        Assert.Contains("catalog.write", p.PermissionCodes());
    }

    [Fact]
    public void RoleProjection_ApplyIfNewer_respects_version()
    {
        var role = RolePermissionsProjection.Create(Tenant, RoleA, "Tenant Admin", 1, ["a"]);
        role.ApplyIfNewer("Tenant Admin", 2, ["catalog.write"]);
        Assert.Equal(2, role.PermissionsVersion);
        Assert.Contains("catalog.write", role.PermissionCodes());
    }
}
