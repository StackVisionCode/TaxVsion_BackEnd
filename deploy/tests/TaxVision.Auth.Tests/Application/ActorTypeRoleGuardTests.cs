using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Fase A1 + Fase 2 (Actor_Type_Authorization_Layers_Plan.md) — ningún usuario debe terminar con
/// un permiso fuera de su actor type colado por un rol mal asignado, en cualquier sentido.
/// Función pura, testeada sin mocks de infraestructura.
/// </summary>
public sealed class ActorTypeRoleGuardTests
{
    private static Permission PortalPermission() =>
        Permission.Seed(Guid.NewGuid(), "portal.folders.view", "portal", "desc", isCustomerPortal: true);

    private static Permission StaffPermission() =>
        Permission.Seed(Guid.NewGuid(), "users.manage", "users", "desc", isCustomerPortal: false);

    private static Permission PlatformOnlyPermission() =>
        Permission.Seed(Guid.NewGuid(), "signature.constraints.manage", "signature", "desc", platformOnly: true);

    private static Role RoleWith(Guid tenantId, params Permission[] permissions)
    {
        var role = Role.Create(tenantId, $"role-{Guid.NewGuid():N}", null).Value;
        role.SetPermissions(permissions.Select(p => p.Id).ToList());
        return role;
    }

    [Fact]
    public void No_roles_is_always_valid()
    {
        var result = ActorTypeRoleGuard.ValidateRolesForActorType(UserActorType.CustomerPortal, [], []);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void CustomerPortal_actor_with_only_portal_permissions_is_accepted()
    {
        var tenantId = Guid.NewGuid();
        var portalPermission = PortalPermission();
        var role = RoleWith(tenantId, portalPermission);

        var result = ActorTypeRoleGuard.ValidateRolesForActorType(
            UserActorType.CustomerPortal,
            [role],
            [portalPermission]
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void CustomerPortal_actor_with_a_staff_permission_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var staffPermission = StaffPermission();
        var role = RoleWith(tenantId, staffPermission);

        var result = ActorTypeRoleGuard.ValidateRolesForActorType(
            UserActorType.CustomerPortal,
            [role],
            [staffPermission]
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NotAssignableToActorType", result.Error.Code);
        Assert.Contains("users.manage", result.Error.Message);
    }

    [Fact]
    public void CustomerPortal_actor_with_a_mixed_role_is_still_rejected()
    {
        var tenantId = Guid.NewGuid();
        var portalPermission = PortalPermission();
        var staffPermission = StaffPermission();
        var role = RoleWith(tenantId, portalPermission, staffPermission);

        var result = ActorTypeRoleGuard.ValidateRolesForActorType(
            UserActorType.CustomerPortal,
            [role],
            [portalPermission, staffPermission]
        );

        Assert.True(result.IsFailure);
        Assert.Contains(staffPermission.Code, result.Error.Message);
    }

    // --- Sentido inverso (nuevo en Fase 2): un actor type staff tampoco puede terminar con un
    // permiso reservado a otro actor type colado por un rol mal asignado. ---

    [Fact]
    public void TenantEmployee_actor_with_a_portal_only_permission_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var portalPermission = PortalPermission();
        var role = RoleWith(tenantId, portalPermission);

        var result = ActorTypeRoleGuard.ValidateRolesForActorType(
            UserActorType.TenantEmployee,
            [role],
            [portalPermission]
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Role.NotAssignableToActorType", result.Error.Code);
        Assert.Contains(portalPermission.Code, result.Error.Message);
    }

    [Fact]
    public void TenantEmployee_actor_with_a_staff_permission_is_accepted()
    {
        var tenantId = Guid.NewGuid();
        var staffPermission = StaffPermission();
        var role = RoleWith(tenantId, staffPermission);

        var result = ActorTypeRoleGuard.ValidateRolesForActorType(
            UserActorType.TenantEmployee,
            [role],
            [staffPermission]
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TenantAdmin_actor_with_a_PlatformOnly_permission_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var platformOnlyPermission = PlatformOnlyPermission();
        var role = RoleWith(tenantId, platformOnlyPermission);

        var result = ActorTypeRoleGuard.ValidateRolesForActorType(
            UserActorType.TenantAdmin,
            [role],
            [platformOnlyPermission]
        );

        Assert.True(result.IsFailure);
        Assert.Contains(platformOnlyPermission.Code, result.Error.Message);
    }

    [Fact]
    public void PlatformAdmin_actor_with_a_PlatformOnly_permission_is_accepted()
    {
        var tenantId = Guid.NewGuid();
        var platformOnlyPermission = PlatformOnlyPermission();
        var role = RoleWith(tenantId, platformOnlyPermission);

        var result = ActorTypeRoleGuard.ValidateRolesForActorType(
            UserActorType.PlatformAdmin,
            [role],
            [platformOnlyPermission]
        );

        Assert.True(result.IsSuccess);
    }

    // --- Regresión Fase 7 (catalogación explícita): el catálogo real, no permisos sintéticos.
    // Este es el escenario que hoy dispara AssignUserRolesHandler/CreateInvitation en producción
    // — asignar uno de los 3 roles de sistema (sembrados 1:1 desde SystemRoleDefaults) al actor
    // type que ese rol representa. Encontró 6 permisos de Communication cuyo AllowedActorTypes
    // inferido (por IsCustomerPortal) no reflejaba que SystemEmployee Y SystemCustomerPortal
    // otorgan el mismo permiso por defecto — el guard rechazaba la asignación del propio rol de
    // sistema antes del fix (ver PermissionCatalog.cs, comentarios junto a CommunicationChatStart
    // et al.). Catálogo completo cargado vía Permission.Seed, sin overrides — si alguien vuelve a
    // introducir esta asimetría (SystemRoleDefaults otorga un permiso que su AllowedActorTypes no
    // cubre), este test falla.
    private static IReadOnlyList<Permission> RealCatalog() =>
        PermissionCatalog
            .All.Select(definition =>
                Permission.Seed(
                    definition.Id,
                    definition.Code,
                    definition.Module,
                    definition.Description,
                    definition.IsCustomerPortal,
                    definition.MinPlanTier,
                    definition.IsAssignableByTenant,
                    definition.PlatformOnly,
                    definition.AllowedActorTypes
                )
            )
            .ToList();

    private static Role SystemRoleWith(Guid tenantId, string systemRoleName, IReadOnlyList<Permission> catalog)
    {
        var role = Role.Create(tenantId, systemRoleName, null, isSystem: true).Value;
        var permissionIds = PermissionCatalog
            .SystemRoleDefaults(systemRoleName)
            .Select(code => catalog.Single(permission => permission.Code == code).Id)
            .ToList();
        role.SetPermissions(permissionIds, seeding: true);
        return role;
    }

    [Theory]
    [InlineData(Role.SystemTenantAdmin, UserActorType.TenantAdmin)]
    [InlineData(Role.SystemEmployee, UserActorType.TenantEmployee)]
    [InlineData(Role.SystemCustomerPortal, UserActorType.CustomerPortal)]
    public void Real_system_role_defaults_are_assignable_to_their_own_actor_type(
        string systemRoleName,
        UserActorType actorType
    )
    {
        var tenantId = Guid.NewGuid();
        var catalog = RealCatalog();
        var role = SystemRoleWith(tenantId, systemRoleName, catalog);

        var result = ActorTypeRoleGuard.ValidateRolesForActorType(actorType, [role], catalog);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }
}
