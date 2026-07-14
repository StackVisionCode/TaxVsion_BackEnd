using TaxVision.Auth.Api.Common;

namespace TaxVision.Auth.Tests.Api;

/// <summary>
/// Fase A6 — v2 doc §26.2 item 2 (aislamiento de login) + item 7 (password-reset no
/// usa Host crudo): AuthController.Login y CredentialsController.ForgotPassword
/// comparten este resolver, así que cubrir esto cubre ambos.
/// </summary>
public sealed class EffectiveLoginTenantResolverTests
{
    [Fact]
    public void Enforced_mode_always_uses_the_resolved_tenant_even_if_the_body_claims_a_different_one()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // El Host resolvió tenantA (oficina real); el cliente manda tenantB en el body
        // (por ejemplo, leyó el TenantId de otra oficina y lo pegó a mano).
        var result = EffectiveLoginTenantResolver.Resolve(
            enforceHostResolution: true,
            resolvedTenantId: tenantA,
            requestedTenantId: tenantB
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantA, result.Value);
        Assert.NotEqual(tenantB, result.Value);
    }

    [Fact]
    public void Enforced_mode_fails_closed_when_the_host_never_resolved()
    {
        var result = EffectiveLoginTenantResolver.Resolve(
            enforceHostResolution: true,
            resolvedTenantId: null,
            requestedTenantId: Guid.NewGuid()
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.NotFound", result.Error.Code);
    }

    [Fact]
    public void Development_mode_accepts_the_explicit_tenant_id_when_there_is_no_resolved_host()
    {
        var tenantId = Guid.NewGuid();

        var result = EffectiveLoginTenantResolver.Resolve(
            enforceHostResolution: false,
            resolvedTenantId: null,
            requestedTenantId: tenantId
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, result.Value);
    }

    [Fact]
    public void Development_mode_fails_when_neither_host_nor_body_provide_a_tenant()
    {
        var result = EffectiveLoginTenantResolver.Resolve(
            enforceHostResolution: false,
            resolvedTenantId: null,
            requestedTenantId: null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Auth.TenantIdRequired", result.Error.Code);
    }
}
