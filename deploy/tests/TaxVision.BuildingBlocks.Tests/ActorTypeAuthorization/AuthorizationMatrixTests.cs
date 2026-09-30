using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// A7 — la tabla de verdad de la autorización. Cada capa ya tenía su test, pero nadie fijaba el
/// resultado de las capas JUNTAS ni en qué orden deciden, que es justamente lo que hay que reconstruir
/// a mano cuando aparece un 403 en producción.
///
/// <para>
/// Las dimensiones son las de §A7 con una aclaración sobre cada una:
/// <list type="bullet">
/// <item><b>actor</b>: el claim <c>actor_type</c>, contra lo que el endpoint declara.</item>
/// <item><b>superficie</b>: un token del Account del Landing no sirve para el CRM.</item>
/// <item><b>permission y deny</b>: son la misma dimensión observable. Desde A2, Auth publica los
/// códigos YA efectivos (roles − denies), así que un permiso denegado sencillamente no está en la
/// proyección — el servicio no distingue "nunca lo tuvo" de "se lo quitaron", y no debe.</item>
/// <item><b>plan</b>: el gate de módulo. Hoy es log-only, así que NO deniega; las filas lo fijan para
/// que el día que A6 lo encienda el cambio se vea acá primero.</item>
/// </list>
/// La <b>asignación</b> no entra: no es una capa compartida sino una regla por servicio (ver
/// <c>CustomerAccessPolicy</c> en Customer y el <c>assignedTo</c> de Billing y SMS), y su matriz vive
/// junto a cada una.
/// </para>
/// </summary>
[Collection(AuthorizationMetricsCollection.Name)]
public sealed class AuthorizationMatrixTests
{
    private const string Permission = "customers.manage";

    /// <summary>Qué respondió la cadena: la primera capa que deniega gana, y con qué razón.</summary>
    private sealed record Decision(bool Allowed, string? Reason);

    private sealed class StaffEndpoint
    {
        [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
        public void Action() { }
    }

    private sealed class FakePermissions(bool granted) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(user.GetActorType() == ActorType.PlatformAdmin || granted);
    }

    private sealed class FakeModules(bool enabled) : ITenantModuleEntitlementsSource
    {
        public Task<bool> IsModuleEnabledAsync(ClaimsPrincipal user, string module, CancellationToken ct = default) =>
            Task.FromResult(enabled);
    }

    private static ClaimsPrincipal Principal(ActorType? actor, string? surface)
    {
        var claims = new List<Claim>();
        if (actor is not null)
            claims.Add(new Claim(ClaimNames.ActorType, actor.Value.ToString()));
        if (surface is not null)
            claims.Add(new Claim(ClaimNames.Surface, surface));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static async Task<Decision> RunAsync(
        ActorType? actor,
        string? surface,
        bool hasPermission,
        bool moduleEnabled,
        bool enforceModuleGate
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AuthorizationMetrics>();
        services.AddSingleton<IUserPermissionsSource>(new FakePermissions(hasPermission));
        services.AddSingleton<ITenantModuleEntitlementsSource>(new FakeModules(moduleEnabled));
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Authorization:ModuleGate:Enforce"] = enforceModuleGate ? "true" : "false",
                    }
                )
                .Build()
        );
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        var provider = services.BuildServiceProvider();

        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = typeof(StaffEndpoint).GetMethod(nameof(StaffEndpoint.Action))!,
            ControllerTypeInfo = typeof(StaffEndpoint).GetTypeInfo(),
        };
        var user = Principal(actor, surface);
        var httpContext = new DefaultHttpContext { User = user, RequestServices = provider };
        var filterContext = new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), descriptor),
            []
        );

        // El orden es el real: superficie → actor type → permission (+ gate de módulo).
        new SurfaceAuthorizationFilter().OnAuthorization(filterContext);
        if (filterContext.Result is not null)
            return Denied(filterContext);

        new ActorTypeAuthorizationFilter().OnAuthorization(filterContext);
        if (filterContext.Result is not null)
            return Denied(filterContext);

        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var result = await authorization.AuthorizeAsync(
            user,
            httpContext,
            HasPermissionAttribute.PolicyPrefix + Permission
        );
        return new Decision(result.Succeeded, result.Succeeded ? null : AuthorizationDenialReasons.Permission);
    }

    private static Decision Denied(AuthorizationFilterContext context)
    {
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(context.Result).Value);
        return new Decision(false, problem.Extensions["reason"]?.ToString());
    }

    public static TheoryData<string, ActorType?, string?, bool, bool, bool, string?> Rows =>
        new()
        {
            // escenario, actor, superficie, permiso, módulo, ¿pasa?, razón
            { "empleado con permiso", ActorType.TenantEmployee, null, true, true, true, null },
            { "admin con permiso", ActorType.TenantAdmin, null, true, true, true, null },
            {
                "empleado sin el permiso (o con deny)",
                ActorType.TenantEmployee,
                null,
                false,
                true,
                false,
                AuthorizationDenialReasons.Permission
            },
            {
                "cliente del portal en un endpoint de staff",
                ActorType.CustomerPortal,
                null,
                true,
                true,
                false,
                AuthorizationDenialReasons.ActorType
            },
            {
                "servicio M2M en un endpoint de staff",
                ActorType.Service,
                null,
                true,
                true,
                false,
                AuthorizationDenialReasons.ActorType
            },
            { "token sin actor_type", null, null, true, true, false, AuthorizationDenialReasons.ActorType },
            {
                "token del Account del Landing",
                ActorType.TenantAdmin,
                AccessSurface.Account,
                true,
                true,
                false,
                AuthorizationDenialReasons.Surface
            },
            // PlatformAdmin pasa el filtro de actor type aunque el endpoint no lo declare.
            { "PlatformAdmin", ActorType.PlatformAdmin, null, false, true, true, null },
            // El plan NO deniega todavía: el gate de módulo es log-only (A6 lo encenderá).
            { "módulo fuera del plan, gate apagado", ActorType.TenantAdmin, null, true, false, true, null },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_chain_decides(
        string scenario,
        ActorType? actor,
        string? surface,
        bool hasPermission,
        bool moduleEnabled,
        bool expectedAllowed,
        string? expectedReason
    )
    {
        var decision = await RunAsync(actor, surface, hasPermission, moduleEnabled, enforceModuleGate: false);

        Assert.True(
            expectedAllowed == decision.Allowed,
            $"{scenario}: se esperaba {(expectedAllowed ? "permitir" : "denegar")} y la cadena "
                + $"{(decision.Allowed ? "permitió" : "denegó")} ({decision.Reason ?? "sin razón"})."
        );
        if (!expectedAllowed)
            Assert.Equal(expectedReason, decision.Reason);
    }

    /// <summary>
    /// La superficie decide ANTES que el actor type: un token del Account con un actor que el endpoint
    /// tampoco admite tiene que responder <c>surface</c>, no <c>actor_type</c>. Si el orden se invierte,
    /// el frontend del Account muestra "acceso restringido" en vez de mandar a reautenticarse.
    /// </summary>
    [Fact]
    public async Task The_surface_answers_before_the_actor_type()
    {
        var decision = await RunAsync(
            ActorType.CustomerPortal,
            AccessSurface.Account,
            hasPermission: true,
            moduleEnabled: true,
            enforceModuleGate: false
        );

        Assert.False(decision.Allowed);
        Assert.Equal(AuthorizationDenialReasons.Surface, decision.Reason);
    }

    /// <summary>
    /// El día que A6 encienda el gate, un módulo fuera del plan deja de ser un log y pasa a cortar.
    /// Se fija acá para que ese cambio sea deliberado y no una sorpresa.
    /// </summary>
    [Fact]
    public async Task With_the_module_gate_enforced_the_plan_does_deny()
    {
        var blocked = await Assert.ThrowsAsync<ModuleUnavailableException>(() =>
            RunAsync(ActorType.TenantAdmin, null, hasPermission: true, moduleEnabled: false, enforceModuleGate: true)
        );

        Assert.Equal("Authz.ModuleUnavailable", blocked.Code);
    }
}
