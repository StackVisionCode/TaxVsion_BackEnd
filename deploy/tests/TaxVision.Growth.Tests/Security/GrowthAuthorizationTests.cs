using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Growth.Api.Authorization;

namespace TaxVision.Growth.Tests.Security;

public sealed class GrowthAuthorizationTests
{
    [Fact]
    public async Task Service_policy_requires_actor_audience_and_exact_scope()
    {
        var policy = await CreateProvider()
            .GetPolicyAsync(HasServiceScopeAttribute.PolicyPrefix + GrowthServiceScopes.CodesQuote);
        Assert.NotNull(policy);

        var complete = Principal(
            new Claim("actor_type", "Service"),
            new Claim(JwtRegisteredClaimNames.Aud, GrowthAuthentication.Audience),
            new Claim("scope", $"{GrowthServiceScopes.CodesQuote} unrelated.scope")
        );
        var wrongScope = Principal(
            new Claim("actor_type", "Service"),
            new Claim(JwtRegisteredClaimNames.Aud, GrowthAuthentication.Audience),
            new Claim("scope", GrowthServiceScopes.CodesReserve)
        );
        var wrongAudience = Principal(
            new Claim("actor_type", "Service"),
            new Claim(JwtRegisteredClaimNames.Aud, "taxvision-payment"),
            new Claim("scope", GrowthServiceScopes.CodesQuote)
        );
        var humanActor = Principal(
            new Claim("actor_type", "User"),
            new Claim(JwtRegisteredClaimNames.Aud, GrowthAuthentication.Audience),
            new Claim("scope", GrowthServiceScopes.CodesQuote)
        );

        Assert.True((await AuthorizeAsync(complete, policy)).Succeeded);
        Assert.False((await AuthorizeAsync(wrongScope, policy)).Succeeded);
        Assert.False((await AuthorizeAsync(wrongAudience, policy)).Succeeded);
        Assert.False((await AuthorizeAsync(humanActor, policy)).Succeeded);
    }

    [Fact]
    public async Task Administrator_role_never_bypasses_explicit_permission_or_service_scope()
    {
        var provider = CreateProvider();
        var permissionPolicy = await provider.GetPolicyAsync(
            HasPermissionAttribute.PolicyPrefix + GrowthPermissions.CodesManage
        );
        var servicePolicy = await provider.GetPolicyAsync(
            HasServiceScopeAttribute.PolicyPrefix + GrowthServiceScopes.CodesCommit
        );
        var administrator = Principal(new Claim(ClaimTypes.Role, "PlatformAdmin"));

        Assert.NotNull(permissionPolicy);
        Assert.NotNull(servicePolicy);
        Assert.False((await AuthorizeAsync(administrator, permissionPolicy)).Succeeded);
        Assert.False((await AuthorizeAsync(administrator, servicePolicy)).Succeeded);
    }

    [Fact]
    public async Task Permission_policy_accepts_only_the_exact_permission_claim()
    {
        var policy = await CreateProvider()
            .GetPolicyAsync(HasPermissionAttribute.PolicyPrefix + GrowthPermissions.ReferralsProgramManage);
        Assert.NotNull(policy);

        var granted = Principal(new Claim("perm", GrowthPermissions.ReferralsProgramManage));
        var differentPermission = Principal(new Claim("perm", GrowthPermissions.ReferralsProgramRead));

        Assert.True((await AuthorizeAsync(granted, policy)).Succeeded);
        Assert.False((await AuthorizeAsync(differentPermission, policy)).Succeeded);
    }

    private static GrowthAuthorizationPolicyProvider CreateProvider() =>
        new(Options.Create(new AuthorizationOptions()));

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    private static async Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal principal, AuthorizationPolicy policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        return await authorization.AuthorizeAsync(principal, resource: null, policy);
    }
}
