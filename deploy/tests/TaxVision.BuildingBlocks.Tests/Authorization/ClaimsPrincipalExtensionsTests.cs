using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Authorization;

public sealed class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetActorType_returns_null_when_claim_is_missing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Null(principal.GetActorType());
    }

    [Fact]
    public void GetActorType_returns_null_when_claim_value_is_unknown()
    {
        var principal = BuildPrincipal(new Claim(ClaimNames.ActorType, "NotARealActorType"));

        Assert.Null(principal.GetActorType());
    }

    [Theory]
    [InlineData("TenantEmployee", ActorType.TenantEmployee)]
    [InlineData("TenantAdmin", ActorType.TenantAdmin)]
    [InlineData("CustomerPortal", ActorType.CustomerPortal)]
    [InlineData("PlatformAdmin", ActorType.PlatformAdmin)]
    [InlineData("Service", ActorType.Service)]
    public void GetActorType_parses_known_values(string claimValue, ActorType expected)
    {
        var principal = BuildPrincipal(new Claim(ClaimNames.ActorType, claimValue));

        Assert.Equal(expected, principal.GetActorType());
    }

    [Fact]
    public void GetActorType_is_case_sensitive_to_avoid_ambiguous_matches()
    {
        var principal = BuildPrincipal(new Claim(ClaimNames.ActorType, "tenantemployee"));

        Assert.Null(principal.GetActorType());
    }

    [Fact]
    public void HasPermission_matches_exact_perm_claim()
    {
        var principal = BuildPrincipal(new Claim(ClaimNames.Permission, "customers.view"));

        Assert.True(principal.HasPermission("customers.view"));
        Assert.False(principal.HasPermission("customers.delete"));
    }

    [Fact]
    public void HasPermission_bypasses_for_a_PlatformAdmin_actor_type_even_without_the_perm_claim()
    {
        var principal = BuildPrincipal(new Claim(ClaimNames.ActorType, nameof(ActorType.PlatformAdmin)));

        Assert.True(principal.HasPermission("anything.at.all"));
    }

    [Fact]
    public void IsPlatformAdmin_reads_the_actor_type_claim()
    {
        var principal = BuildPrincipal(new Claim(ClaimNames.ActorType, nameof(ActorType.PlatformAdmin)));

        Assert.True(principal.IsPlatformAdmin());
    }

    [Theory]
    [InlineData("PlatformAdmin")]
    [InlineData("Platform Admin")]
    [InlineData("platformadmin")]
    public void IsPlatformAdmin_ignores_a_tenant_role_named_like_the_platform_pseudo_role(string roleName)
    {
        // El claim de rol mezcla el pseudo-rol del actor type con los nombres de los custom roles
        // del tenant: nombrar un custom role así no puede otorgar ámbito de plataforma.
        var principal = BuildPrincipal(
            new Claim(ClaimNames.ActorType, nameof(ActorType.TenantAdmin)),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim(ClaimTypes.Role, roleName)
        );

        Assert.False(principal.IsPlatformAdmin());
        Assert.False(principal.HasPermission("anything.at.all"));
    }

    [Fact]
    public void IsPlatformAdmin_is_false_without_an_actor_type_claim()
    {
        var principal = BuildPrincipal(new Claim(ClaimTypes.Role, "PlatformAdmin"));

        Assert.False(principal.IsPlatformAdmin());
    }

    [Theory]
    [InlineData("TenantEmployee")]
    [InlineData("TenantAdmin")]
    [InlineData("CustomerPortal")]
    [InlineData("Service")]
    public void IsPlatformAdmin_is_false_for_every_other_actor_type(string actorType)
    {
        var principal = BuildPrincipal(new Claim(ClaimNames.ActorType, actorType));

        Assert.False(principal.IsPlatformAdmin());
    }

    [Fact]
    public void TryGetTenantId_parses_the_tenant_id_claim()
    {
        var tenantId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(ClaimNames.TenantId, tenantId.ToString()));

        Assert.True(principal.TryGetTenantId(out var parsed));
        Assert.Equal(tenantId, parsed);
    }

    [Fact]
    public void TryGetTenantId_fails_when_claim_is_missing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False(principal.TryGetTenantId(out _));
    }

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));
}
