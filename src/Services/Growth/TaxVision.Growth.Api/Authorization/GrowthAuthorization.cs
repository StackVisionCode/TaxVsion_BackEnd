using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TaxVision.Growth.Api.Common;

namespace TaxVision.Growth.Api.Authorization;

// RBAC Fase 8 (RBAC_Hardening_Plan.md) — Growth se suma al HasPermissionAttribute compartido de
// BuildingBlocks.ActorTypeAuthorization (mismo PolicyPrefix "perm:") en vez de mantener una copia
// local idéntica. La copia local existía únicamente para que GrowthAuthorizationPolicyProvider
// pudiera reconocer el prefijo de policy sin depender de BuildingBlocks.Web — con el provider
// ahora delegando esa rama a PermissionPolicyProvider (ver más abajo), ya no hace falta.
// HasServiceScopeAttribute NO migra — es un mecanismo de M2M (client-credentials, audience+scope)
// sin relación con permisos de usuario, y sigue siendo exclusivo de Growth.

public sealed class HasServiceScopeAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "service-scope:";

    public HasServiceScopeAttribute(string scope)
        : base($"{PolicyPrefix}{scope}") { }
}

public static class GrowthAuthentication
{
    public const string Audience = "taxvision-growth";
}

/// <summary>
/// Dos policies dinámicas completamente distintas conviven en un único
/// <see cref="IAuthorizationPolicyProvider"/> porque ASP.NET Core solo permite registrar uno por
/// aplicación:
/// <list type="bullet">
/// <item><description><c>perm:</c> (<see cref="BuildingBlocks.ActorTypeAuthorization.HasPermissionAttribute"/>)
/// — permisos de usuario humano/PlatformAdmin. RBAC Fase 8: delegada byte a byte a
/// <see cref="PermissionPolicyProvider"/> (composición, no se reimplementa la lógica acá) — mismo
/// comportamiento que los otros 13 microservicios: resuelve <c>IUserPermissionsSource</c> desde DI
/// (Jwt embebido u proyección local según <c>Authorization:PermissionsSource</c>), con el bypass de
/// PlatformAdmin que ya trae <c>ClaimsPrincipalExtensions.HasPermission</c> de BuildingBlocks.</description></item>
/// <item><description><c>service-scope:</c> (<see cref="HasServiceScopeAttribute"/>) — client-credentials
/// M2M (Audience+scope), sin relación con permisos de usuario ni con IUserPermissionsSource. Roles
/// nunca bypasean esta policy: un servicio debe recibir el claim <c>scope</c> explícito de Auth.
/// Intacta, no tocada por la migración de Fase 8.</description></item>
/// </list>
/// </summary>
public sealed class GrowthAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
{
    // No se resuelve por DI: PermissionPolicyProvider solo depende de las mismas IOptions que ya
    // recibe este constructor, y ASP.NET Core exige exactamente UN IAuthorizationPolicyProvider
    // registrado por app — no se puede registrar PermissionPolicyProvider por separado sin
    // pisar este. Instanciarlo acá reutiliza su lógica (RequireAssertion contra
    // IUserPermissionsSource resuelto desde HttpContext.RequestServices) sin duplicarla.
    private readonly PermissionPolicyProvider _permissionPolicyProvider;

    public GrowthAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
        _permissionPolicyProvider = new PermissionPolicyProvider(options);
    }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return await _permissionPolicyProvider.GetPolicyAsync(policyName);

        if (policyName.StartsWith(HasServiceScopeAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var scope = policyName[HasServiceScopeAttribute.PolicyPrefix.Length..];
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim("actor_type", "Service")
                .RequireAssertion(context =>
                    context.User.HasAudience(GrowthAuthentication.Audience) && context.User.HasScope(scope)
                )
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}
