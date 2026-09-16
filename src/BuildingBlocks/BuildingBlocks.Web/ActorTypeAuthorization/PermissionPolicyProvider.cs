using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

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

                    var metrics = httpContext.RequestServices.GetRequiredService<AuthorizationMetrics>();
                    metrics.RecordDecision(allowed, "1");

                    // Gate de Entitlements/módulo: si el permiso pasó pero pertenece a un módulo que el
                    // plan del tenant no habilita, loguea (log-only) o lanza 403 según el flag
                    // Authorization:ModuleGate:Enforce. Opt-in: solo corre donde se registró
                    // ITenantModuleEntitlementsSource; un permiso transversal (ModuleFor == null) no se toca.
                    if (allowed)
                    {
                        var moduleSource = httpContext.RequestServices.GetService<ITenantModuleEntitlementsSource>();
                        var module = PermissionModuleMap.ModuleFor(permission);
                        if (moduleSource is not null && module is not null)
                        {
                            var moduleEnabled = await moduleSource.IsModuleEnabledAsync(
                                context.User,
                                module,
                                httpContext.RequestAborted
                            );
                            metrics.RecordModuleDecision(moduleEnabled, module);
                            if (!moduleEnabled)
                            {
                                var enforce = httpContext
                                    .RequestServices.GetRequiredService<IConfiguration>()
                                    .GetValue("Authorization:ModuleGate:Enforce", false);
                                if (enforce)
                                    throw new ModuleUnavailableException(
                                        "Authz.ModuleUnavailable",
                                        $"Your plan does not include the '{module}' module required for this action."
                                    );

                                httpContext
                                    .RequestServices.GetRequiredService<ILoggerFactory>()
                                    .CreateLogger("BuildingBlocks.Web.ModuleGate")
                                    .LogInformation(
                                        "Module gate (log-only): permission {Permission} belongs to module {Module} "
                                            + "which is NOT enabled for the tenant; would 403 once enforced.",
                                        permission,
                                        module
                                    );
                            }
                        }
                    }

                    return allowed;
                })
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}
