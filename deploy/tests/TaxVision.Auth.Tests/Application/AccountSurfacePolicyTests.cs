using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>
/// Quién llega a tener un token del Account. Es la puerta que de verdad decide el acceso por actor: varios
/// endpoints que el Account consume (asientos, cotizaciones) también los usa el CRM y por eso aceptan
/// empleados — lo que impide que un empleado los alcance desde el Landing es esta política, no el endpoint.
/// </summary>
public sealed class AccountSurfacePolicyTests
{
    [Theory]
    [InlineData(UserActorType.TenantEmployee)]
    [InlineData(UserActorType.CustomerPortal)]
    public void Only_office_admins_get_an_account_session(UserActorType actorType)
    {
        var denied = AccountSurfacePolicy.Check(Admin(actorType), mustEnrollMfa: false);

        Assert.Equal(AccountSurfacePolicy.AdminOnly, denied);
    }

    // El PlatformAdmin opera por sus herramientas de soporte, no por el Account de una oficina.
    [Fact]
    public void The_platform_admin_does_not_enter_the_account_of_an_office()
    {
        var platformAdmin = Admin(UserActorType.PlatformAdmin, PlatformTenant.Id);

        Assert.Equal(AccountSurfacePolicy.AdminOnly, AccountSurfacePolicy.Check(platformAdmin, mustEnrollMfa: false));
    }

    [Fact]
    public void The_office_admin_enters()
    {
        Assert.Null(AccountSurfacePolicy.Check(Admin(UserActorType.TenantAdmin), mustEnrollMfa: false));
    }

    // El Account no enrola MFA: si la oficina la exige y el admin no la tiene, primero pasa por el workspace.
    [Fact]
    public void An_admin_who_still_has_to_set_up_mfa_goes_to_the_workspace_first()
    {
        var denied = AccountSurfacePolicy.Check(Admin(UserActorType.TenantAdmin), mustEnrollMfa: true);

        Assert.Equal(AccountSurfacePolicy.MfaSetupRequired, denied);
    }

    private static User Admin(UserActorType actorType, Guid? tenantId = null) =>
        User.Register(
            tenantId ?? Guid.NewGuid(),
            "Ada",
            "Lovelace",
            "ada@acme.test",
            "hash",
            actorType,
            customerId: actorType == UserActorType.CustomerPortal ? Guid.NewGuid() : null
        ).Value;
}
