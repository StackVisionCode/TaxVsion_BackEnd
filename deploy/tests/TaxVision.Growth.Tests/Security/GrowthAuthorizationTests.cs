using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Growth.Api.Authorization;
using HasPermissionAttribute = BuildingBlocks.ActorTypeAuthorization.HasPermissionAttribute;

namespace TaxVision.Growth.Tests.Security;

/// <summary>
/// RBAC Fase 8 (RBAC_Hardening_Plan.md) — Growth adoptó el HasPermissionAttribute compartido de
/// BuildingBlocks.ActorTypeAuthorization; GrowthAuthorizationPolicyProvider ahora DELEGA la rama
/// "perm:" a PermissionPolicyProvider (BuildingBlocks) en vez de reimplementarla. Estos tests
/// verifican la composición (perm: delega correctamente, service-scope: sigue intacto) — el
/// comportamiento fino de IUserPermissionsSource (Jwt/Projection, perm_v staleness, bypasses) ya
/// está cubierto en TaxVision.BuildingBlocks.Tests/ActorTypeAuthorization/PermissionsSourceTests.cs.
/// </summary>
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
    public async Task PlatformAdmin_bypasses_the_permission_policy_but_never_the_service_scope_policy()
    {
        // RBAC Fase 8: antes de esta fase NINGUNA de las dos policies de Growth bypaseaba
        // PlatformAdmin ("Roles never bypass either policy", ver GrowthAuthorization.cs anterior).
        // Al delegar "perm:" a PermissionPolicyProvider (BuildingBlocks), Growth ahora se comporta
        // como los otros 13 microservicios: ClaimsPrincipalExtensions.HasPermission (compartida)
        // siempre incluye el bypass de PlatformAdmin. "service-scope:" es un mecanismo M2M
        // completamente distinto (client-credentials, sin relación con roles humanos) y sigue sin
        // bypass — un PlatformAdmin autenticado como usuario humano nunca es, por sí solo, un
        // client de servicio válido.
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
        Assert.True((await AuthorizeAsync(administrator, permissionPolicy)).Succeeded);
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

    [Fact]
    public async Task Permission_policy_resolves_IUserPermissionsSource_from_the_HttpContext_when_available()
    {
        // Reproduce el camino real de un request HTTP (PermissionPolicyProvider.GetPolicyAsync
        // solo cae al ClaimsPrincipal.HasPermission "de siempre" cuando context.Resource NO es un
        // HttpContext — ver los otros tests de esta clase, que pasan resource: null a propósito).
        // Este test confirma que la composición de GrowthAuthorizationPolicyProvider realmente
        // resuelve IUserPermissionsSource vía HttpContext.RequestServices, igual que los otros 13
        // microservicios — sin este test, un typo en la delegación (ej. devolver la policy sin
        // el RequireAssertion correcto) pasaría inadvertido.
        var policy = await CreateProvider()
            .GetPolicyAsync(HasPermissionAttribute.PolicyPrefix + GrowthPermissions.CodesManage);
        Assert.NotNull(policy);

        var granted = Principal(new Claim("perm", GrowthPermissions.CodesManage));
        var denied = Principal(new Claim("perm", GrowthPermissions.CodesRead));

        Assert.True((await AuthorizeViaHttpContextAsync(granted, policy)).Succeeded);
        Assert.False((await AuthorizeViaHttpContextAsync(denied, policy)).Succeeded);
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

    private static async Task<AuthorizationResult> AuthorizeViaHttpContextAsync(
        ClaimsPrincipal principal,
        AuthorizationPolicy policy
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddScoped<IUserPermissionsSource, JwtEmbeddedPermissionsSource>();
        services.AddSingleton<AuthorizationMetrics>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = principal };

        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        return await authorization.AuthorizeAsync(principal, httpContext, policy);
    }
}
