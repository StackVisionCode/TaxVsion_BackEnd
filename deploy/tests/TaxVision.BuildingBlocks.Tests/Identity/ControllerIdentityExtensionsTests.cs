using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Identity;

public sealed class ControllerIdentityExtensionsTests
{
    private sealed class FakeController : ControllerBase { }

    private static FakeController BuildController(params Claim[] claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
        return new FakeController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
        };
    }

    [Fact]
    public void TryGetTenantAndUser_returns_true_when_both_claims_present()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var controller = BuildController(
            new Claim(ClaimNames.TenantId, tenantId.ToString()),
            new Claim("sub", userId.ToString())
        );

        var result = controller.TryGetTenantAndUser(out var resolvedTenantId, out var resolvedUserId);

        Assert.True(result);
        Assert.Equal(tenantId, resolvedTenantId);
        Assert.Equal(userId, resolvedUserId);
    }

    [Fact]
    public void TryGetTenantAndUser_fails_when_tenant_claim_missing()
    {
        var controller = BuildController(new Claim("sub", Guid.NewGuid().ToString()));

        var result = controller.TryGetTenantAndUser(out var tenantId, out var userId);

        Assert.False(result);
        Assert.Equal(Guid.Empty, tenantId);
        Assert.Equal(Guid.Empty, userId);
    }

    [Fact]
    public void TryGetTenantAndUser_fails_when_user_claim_missing()
    {
        var controller = BuildController(new Claim(ClaimNames.TenantId, Guid.NewGuid().ToString()));

        var result = controller.TryGetTenantAndUser(out _, out var userId);

        Assert.False(result);
        Assert.Equal(Guid.Empty, userId);
    }

    [Fact]
    public void TryResolveTenantId_allows_exact_match_for_non_admin()
    {
        var tenantId = Guid.NewGuid();
        var controller = BuildController(new Claim(ClaimNames.TenantId, tenantId.ToString()));

        var result = controller.TryResolveTenantId(tenantId, out var resolvedTenantId);

        Assert.True(result);
        Assert.Equal(tenantId, resolvedTenantId);
    }

    [Fact]
    public void TryResolveTenantId_denies_cross_tenant_access_for_non_admin()
    {
        var tokenTenantId = Guid.NewGuid();
        var requestedTenantId = Guid.NewGuid();
        var controller = BuildController(new Claim(ClaimNames.TenantId, tokenTenantId.ToString()));

        var result = controller.TryResolveTenantId(requestedTenantId, out var resolvedTenantId);

        Assert.False(result);
        Assert.Equal(Guid.Empty, resolvedTenantId);
    }

    [Fact]
    public void TryResolveTenantId_allows_PlatformAdmin_to_bypass_tenant_mismatch()
    {
        var requestedTenantId = Guid.NewGuid();
        var controller = BuildController(new Claim(ClaimTypes.Role, "PlatformAdmin"));

        var result = controller.TryResolveTenantId(requestedTenantId, out var resolvedTenantId);

        Assert.True(result);
        Assert.Equal(requestedTenantId, resolvedTenantId);
    }

    [Fact]
    public void TryResolveTenantId_nullable_overload_falls_back_to_token_tenant_when_null()
    {
        var tokenTenantId = Guid.NewGuid();
        var controller = BuildController(new Claim(ClaimNames.TenantId, tokenTenantId.ToString()));

        var result = controller.TryResolveTenantId((Guid?)null, out var resolvedTenantId);

        Assert.True(result);
        Assert.Equal(tokenTenantId, resolvedTenantId);
    }

    [Fact]
    public void TryResolveTenantId_nullable_overload_delegates_to_non_nullable_when_value_present()
    {
        var tokenTenantId = Guid.NewGuid();
        var requestedTenantId = Guid.NewGuid();
        var controller = BuildController(new Claim(ClaimNames.TenantId, tokenTenantId.ToString()));

        var result = controller.TryResolveTenantId((Guid?)requestedTenantId, out var resolvedTenantId);

        Assert.False(result);
        Assert.Equal(Guid.Empty, resolvedTenantId);
    }
}
