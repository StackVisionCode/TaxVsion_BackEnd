using BuildingBlocks.Authorization;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Permissions;

// Administración de credenciales por la oficina: solo el TenantAdmin raíz, nunca roles custom ni empleados.
public sealed class CredentialAdministrationPermissionsTests
{
    public static TheoryData<string, string, string> Permissions =>
        new()
        {
            { "users.password.send_reset", "users", "a1000000-0000-0000-0000-000000000234" },
            { "users.password.require_change", "users", "a1000000-0000-0000-0000-000000000235" },
            { "users.credentials.secure", "users", "a1000000-0000-0000-0000-000000000236" },
            { "users.mfa.reset", "users", "a1000000-0000-0000-0000-000000000237" },
            { "customers.portal_credentials.send_reset", "customers", "a1000000-0000-0000-0000-000000000238" },
            { "customers.portal_credentials.require_change", "customers", "a1000000-0000-0000-0000-000000000239" },
            { "customers.portal_credentials.secure", "customers", "a1000000-0000-0000-0000-00000000023a" },
            { "customers.portal_mfa.reset", "customers", "a1000000-0000-0000-0000-00000000023b" },
        };

    [Theory]
    [MemberData(nameof(Permissions))]
    public void Each_permission_exists_with_its_fixed_id_and_admin_only_flags(string code, string module, string id)
    {
        var definition = Assert.Single(PermissionCatalog.All, d => d.Code == code);

        Assert.Equal(new Guid(id), definition.Id);
        Assert.Equal(module, definition.Module);
        Assert.True(definition.IsDangerous);
        Assert.False(definition.IsAssignableByTenant);
        Assert.False(definition.IsCustomerPortal);
        Assert.False(definition.PlatformOnly);
        Assert.Equal(new[] { UserActorType.TenantAdmin }, definition.EffectiveAllowedActorTypes);
    }

    [Theory]
    [MemberData(nameof(Permissions))]
    public void Only_the_tenant_admin_root_role_receives_it_once_its_endpoint_exists(
        string code,
        string module,
        string id
    )
    {
        _ = (module, id);
        var definition = Assert.Single(PermissionCatalog.All, d => d.Code == code);

        // Reservado (sin endpoint) no lo recibe nadie; liberado, solo el rol de sistema del TenantAdmin raíz.
        Assert.Equal(!definition.IsReserved, PermissionCatalog.SystemTenantAdminRootPermissions().Contains(code));
        Assert.DoesNotContain(code, PermissionCatalog.SystemRoleDefaults(Role.SystemEmployee));
        Assert.DoesNotContain(code, PermissionCatalog.SystemRoleDefaults(Role.SystemCustomerPortal));
        Assert.DoesNotContain(code, PermissionCatalog.SystemRoleDefaults(Role.SystemTenantAdmin));
    }

    [Theory]
    [MemberData(nameof(Permissions))]
    public void Client_permissions_are_gated_by_the_customers_module(string code, string module, string id)
    {
        _ = id;
        var definition = Assert.Single(PermissionCatalog.All, d => d.Code == code);

        Assert.Equal(module == "customers" ? "customers" : null, definition.GatedModule);
        Assert.Equal(definition.GatedModule, PermissionModuleMap.ModuleFor(code));
    }

    [Fact]
    public void Client_codes_are_declared_in_building_blocks_for_the_customer_service()
    {
        Assert.Equal("customers.portal_credentials.send_reset", CustomersPermissions.PortalCredentialsSendReset);
        Assert.Equal(
            "customers.portal_credentials.require_change",
            CustomersPermissions.PortalCredentialsRequireChange
        );
        Assert.Equal("customers.portal_credentials.secure", CustomersPermissions.PortalCredentialsSecure);
        Assert.Equal("customers.portal_mfa.reset", CustomersPermissions.PortalMfaReset);
        Assert.Equal("users.password.send_reset", PermissionCatalog.UsersPasswordSendReset);
        Assert.Equal("users.password.require_change", PermissionCatalog.UsersPasswordRequireChange);
        Assert.Equal("users.credentials.secure", PermissionCatalog.UsersCredentialsSecure);
        Assert.Equal("users.mfa.reset", PermissionCatalog.UsersMfaReset);
    }

    [Fact]
    public void The_temporary_code_permission_is_not_part_of_this_delivery()
    {
        Assert.DoesNotContain(PermissionCatalog.All, d => d.Code == "users.access.temporary_code");
    }
}
