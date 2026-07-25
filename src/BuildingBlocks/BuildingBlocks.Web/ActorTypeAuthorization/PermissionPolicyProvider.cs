using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Intercepta las policies con prefijo <see cref="HasPermissionAttribute.PolicyPrefix"/> y las
/// construye contra <see cref="IUserPermissionsSource"/> (RBAC Fase 7 — antes llamaba
/// directamente a <see cref="ClaimsPrincipalExtensions.HasPermission"/>; ese sigue siendo el
/// comportamiento default vía <see cref="JwtEmbeddedPermissionsSource"/>, resuelto por DI en vez
/// de codeado a fuego, para que un servicio pueda optar por <see cref="ProjectionPermissionsSource"/>
/// sin tocar este archivo). Cada microservicio que use <see cref="HasPermissionAttribute"/> debe
/// registrar esta clase como <c>IAuthorizationPolicyProvider</c> en su <c>Program.cs</c> (Fase 3 —
/// reemplaza a la copia local que tenía cada uno).
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[HasPermissionAttribute.PolicyPrefix.Length..];
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(async context =>
                {
                    // context.Resource solo es un HttpContext real dentro de un request HTTP
                    // (no en los pocos lugares donde se evalúa una policy fuera de ese contexto,
                    // p.ej. tests unitarios que arman su propio AuthorizationHandlerContext) — sin
                    // RequestServices no hay forma de resolver IUserPermissionsSource ni
                    // AuthorizationMetrics, así que se cae al comportamiento default histórico
                    // (sin métrica) en vez de reventar.
                    if (context.Resource is not HttpContext httpContext)
                        return context.User.HasPermission(permission);

                    var source = httpContext.RequestServices.GetRequiredService<IUserPermissionsSource>();
                    var allowed = await source.HasPermissionAsync(context.User, permission, httpContext.RequestAborted);

                    httpContext.RequestServices.GetRequiredService<AuthorizationMetrics>().RecordDecision(allowed, "1");
                    return allowed;
                })
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}
