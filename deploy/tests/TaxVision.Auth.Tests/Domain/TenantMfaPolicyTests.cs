using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>
/// Fase A1 — confirma que la política MFA por tenant distingue correctamente al
/// Tenant Customer (portal) del resto de actores, y que admins siempre requieren MFA.
/// </summary>
public sealed class TenantMfaPolicyTests
{
    [Fact]
    public void Admins_always_require_mfa_by_default()
    {
        var policy = TenantMfaPolicy.CreateDefault(Guid.NewGuid());

        Assert.True(policy.RequiresFor(UserActorType.TenantAdmin));
        Assert.True(policy.RequiresFor(UserActorType.PlatformAdmin));
    }

    [Fact]
    public void Customer_portal_does_not_require_mfa_by_default()
    {
        var policy = TenantMfaPolicy.CreateDefault(Guid.NewGuid());

        Assert.False(policy.RequiresFor(UserActorType.CustomerPortal));
    }

    [Fact]
    public void Tenant_can_opt_into_requiring_mfa_for_the_customer_portal()
    {
        var policy = TenantMfaPolicy.CreateDefault(Guid.NewGuid());

        var result = policy.Update(requireForEmployees: false, requireForCustomerPortal: true, trustedDeviceDays: 30);

        Assert.True(result.IsSuccess);
        Assert.True(policy.RequiresFor(UserActorType.CustomerPortal));
        Assert.False(policy.RequiresFor(UserActorType.TenantEmployee));
    }

    [Fact]
    public void Admin_mfa_requirement_cannot_be_turned_off_through_update()
    {
        // Update() no expone requireForAdmins — es intencional, ver comentario del agregado:
        // "MFA para administradores es obligatorio por diseño y no puede desactivarse".
        var policy = TenantMfaPolicy.CreateDefault(Guid.NewGuid());

        policy.Update(requireForEmployees: true, requireForCustomerPortal: true, trustedDeviceDays: 10);

        Assert.True(policy.RequiresFor(UserActorType.TenantAdmin));
    }
}
