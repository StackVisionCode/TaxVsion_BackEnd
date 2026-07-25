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
    public void HasPermission_bypasses_for_PlatformAdmin_role_even_without_the_perm_claim()
    {
        var principal = BuildPrincipal(new Claim(ClaimTypes.Role, "PlatformAdmin"));

        Assert.True(principal.HasPermission("anything.at.all"));
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
