using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Fase A1 — un Tenant Customer (portal) nunca debe terminar con un permiso interno
/// colado por un rol mal asignado. Función pura, testeada sin mocks de infraestructura.
/// </summary>
public sealed class CustomerPortalRoleGuardTests
{
    private static Permission PortalPermission() =>
        Permission.Seed(Guid.NewGuid(), "portal.folders.view", "portal", "desc", isCustomerPortal: true);

    private static Permission InternalPermission() =>
        Permission.Seed(Guid.NewGuid(), "users.manage", "users", "desc", isCustomerPortal: false);

    private static Role RoleWith(Guid tenantId, params Permission[] permissions)
    {
        var role = Role.Create(tenantId, $"role-{Guid.NewGuid():N}", null).Value;
        role.SetPermissions(permissions.Select(p => p.Id).ToList());
        return role;
    }

    [Fact]
    public void No_roles_is_always_valid()
    {
        var result = CustomerPortalRoleGuard.ValidateRolesForCustomerPortal([], []);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Role_with_only_portal_permissions_is_accepted()
    {
        var tenantId = Guid.NewGuid();
        var portalPermission = PortalPermission();
        var role = RoleWith(tenantId, portalPermission);

        var result = CustomerPortalRoleGuard.ValidateRolesForCustomerPortal([role], [portalPermission]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Role_with_an_internal_permission_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var internalPermission = InternalPermission();
        var role = RoleWith(tenantId, internalPermission);

        var result = CustomerPortalRoleGuard.ValidateRolesForCustomerPortal([role], [internalPermission]);

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NotAssignableToCustomerPortal", result.Error.Code);
        Assert.Contains("users.manage", result.Error.Message);
    }

    [Fact]
    public void Mixed_role_with_one_internal_permission_among_several_is_still_rejected()
    {
        var tenantId = Guid.NewGuid();
        var portalPermission = PortalPermission();
        var internalPermission = InternalPermission();
        var role = RoleWith(tenantId, portalPermission, internalPermission);

        var result = CustomerPortalRoleGuard.ValidateRolesForCustomerPortal(
            [role],
            [portalPermission, internalPermission]
        );

        Assert.True(result.IsFailure);
        Assert.Contains(internalPermission.Code, result.Error.Message);
    }
}
