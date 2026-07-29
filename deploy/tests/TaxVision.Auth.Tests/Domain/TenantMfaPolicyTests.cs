using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>
/// Fase A1 — confirma que la política MFA por tenant distingue correctamente al
/// Tenant Customer (portal) del resto de actores. MFA es opt-in por defecto para
/// todos los actor types, incluidos los admins (el enrolamiento se activa desde ajustes).
/// </summary>
public sealed class TenantMfaPolicyTests
{
    [Fact]
    public void Admins_do_not_require_mfa_by_default()
    {
        var policy = TenantMfaPolicy.CreateDefault(Guid.NewGuid());

        Assert.False(policy.RequiresFor(UserActorType.TenantAdmin));
        Assert.False(policy.RequiresFor(UserActorType.PlatformAdmin));
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
    public void Admin_mfa_requirement_is_not_affected_by_update()
    {
        // Update() no expone requireForAdmins — no hay forma de activar/desactivar MFA
        // de admins vía este método hoy (ver gap funcional documentado en 00_Baseline.md §7.2).
        var policy = TenantMfaPolicy.CreateDefault(Guid.NewGuid());

        policy.Update(requireForEmployees: true, requireForCustomerPortal: true, trustedDeviceDays: 10);

        Assert.False(policy.RequiresFor(UserActorType.TenantAdmin));
    }
}
