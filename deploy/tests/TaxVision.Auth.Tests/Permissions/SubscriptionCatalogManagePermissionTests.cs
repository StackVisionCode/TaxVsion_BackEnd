using BuildingBlocks.Authorization;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Tests.Permissions;

// D13: tocar el catálogo de planes es solo de plataforma; ninguna oficina ni el portal lo reciben.
public sealed class SubscriptionCatalogManagePermissionTests
{
    [Fact]
    public void The_catalog_permission_is_platform_only()
    {
        var definition = Assert.Single(
            PermissionCatalog.All,
            permission => permission.Code == SubscriptionPermissions.CatalogManage
        );

        Assert.True(definition.PlatformOnly);
        Assert.False(definition.IsAssignableByTenant);
        Assert.False(definition.IsCustomerPortal);
    }

    [Theory]
    [InlineData(Role.SystemTenantAdmin)]
    [InlineData(Role.SystemEmployee)]
    [InlineData(Role.SystemCustomerPortal)]
    public void No_office_or_portal_role_gets_it_by_default(string role)
    {
        Assert.DoesNotContain(SubscriptionPermissions.CatalogManage, PermissionCatalog.SystemRoleDefaults(role));
    }
}
