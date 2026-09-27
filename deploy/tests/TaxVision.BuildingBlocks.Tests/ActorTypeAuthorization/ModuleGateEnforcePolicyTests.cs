using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// A6 — el gate de módulo recorrido de punta a punta por el camino REAL:
/// <see cref="PermissionPolicyProvider"/> con un <see cref="HttpContext"/> de verdad y sus servicios
/// resueltos por DI, que es la única forma de que la rama del gate se ejecute (con
/// <c>resource: null</c> la policy cae al comportamiento default y el gate no corre — por eso los
/// tests de policy que ya existían no lo cubrían).
///
/// El plan pedía explícitamente "tests con Enforce=true (hoy no existe ninguna)". Lo que se fija acá es
/// el orden del pipeline y las dos direcciones del flag:
///
/// <list type="bullet">
/// <item>el módulo se evalúa DESPUÉS de conceder el permiso, así que <c>Authz.ModuleUnavailable</c>
/// solo le aparece a quien "podría si el plan lo tuviera" — es lo que necesita la UX de upgrade, y
/// evita revelar qué módulos tiene el plan a quien no tiene acceso;</item>
/// <item>en log-only el gate NO cambia la decisión, que es lo que permite medir antes de encender;</item>
/// <item>un permiso transversal o exento no pasa por el gate ni siquiera con el flag encendido.</item>
/// </list>
/// </summary>
[Collection(AuthorizationMetricsCollection.Name)]
public sealed class ModuleGateEnforcePolicyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    /// <summary>Evalúa la policy real contra un HttpContext real. Devuelve la decisión, o la excepción
    /// del gate si el escalón la lanzó.</summary>
    private static async Task<(bool Allowed, ModuleUnavailableException? Denial)> EvaluateAsync(
        string permission,
        IReadOnlyList<string>? enabledModules,
        bool enforce,
        string[]? enforcedModules = null,
        bool grantPermission = true
    )
    {
        var settings = new Dictionary<string, string?>();
        if (enforce)
            settings[ModuleGateSettings.EnforceKey] = "true";
        if (enforcedModules is not null)
        {
            for (var i = 0; i < enforcedModules.Length; i++)
                settings[$"{ModuleGateSettings.EnforcedModulesKey}:{i}"] = enforcedModules[i];
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton<IUserPermissionsSource>(new FixedPermissionsSource(grantPermission));
        // La FUENTE es la real: la regla `null` = no se sabe (no gatea) vs `[]` = no tiene nada
        // (gatea) vive ahí y no se vuelve a escribir acá — copiarla en un doble haría que el test
        // comprobara su propia copia. Lo que se sustituye es solo el lector de la proyección.
        services.AddSingleton<ITenantEntitlementModulesReader>(new FixedModulesReader(enabledModules));
        services.AddSingleton<ITenantModuleEntitlementsSource, TenantModuleEntitlementsSource>();
        services.AddSingleton<AuthorizationMetrics>();
        services.AddLogging();

        await using var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim("tenant_id", TenantId.ToString()),
                    new Claim("actor_type", nameof(ActorType.TenantEmployee)),
                ],
                "Bearer"
            )
        );
        httpContext.User = principal;

        var policyProvider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));
        var policy = await policyProvider.GetPolicyAsync($"{HasPermissionAttribute.PolicyPrefix}{permission}");
        Assert.NotNull(policy);

        var requirement = policy!.Requirements.OfType<AssertionRequirement>().Single();
        var context = new AuthorizationHandlerContext([requirement], principal, httpContext);

        try
        {
            await requirement.HandleAsync(context);
        }
        catch (ModuleUnavailableException denial)
        {
            return (false, denial);
        }

        return (context.HasSucceeded, null);
    }

    [Fact]
    public async Task With_the_module_enforced_a_granted_permission_is_denied_with_the_module_code()
    {
        // El caso que el plan quiere garantizado: el permiso está concedido y el plan no incluye el
        // módulo. Antes de A6 esto solo dejaba una línea en el log.
        var (allowed, denial) = await EvaluateAsync(
            "campaigns.manage",
            enabledModules: ["documents", "planner"],
            enforce: true,
            enforcedModules: ["campaigns"]
        );

        Assert.False(allowed);
        Assert.NotNull(denial);
        Assert.Equal("campaigns", denial!.Module);
        Assert.Equal("Authz.ModuleUnavailable", denial.Code);
    }

    [Fact]
    public async Task With_the_module_in_log_only_the_same_request_passes()
    {
        // La garantía del despliegue escalonado: medir no deniega. Si esto fallara, encender la
        // medición cortaría a tenants que sí pagaron.
        var (allowed, denial) = await EvaluateAsync(
            "campaigns.manage",
            enabledModules: ["documents", "planner"],
            enforce: false
        );

        Assert.True(allowed);
        Assert.Null(denial);
    }

    [Fact]
    public async Task A_module_outside_the_step_stays_in_log_only_even_with_the_flag_on()
    {
        // El escalón en sí: con `campaigns` encendido, un tenant sin `comms` NO recibe 403 en comms.
        var (allowed, denial) = await EvaluateAsync(
            "communication.chat.start",
            enabledModules: ["documents"],
            enforce: true,
            enforcedModules: ["campaigns"]
        );

        Assert.True(allowed);
        Assert.Null(denial);
    }

    [Fact]
    public async Task The_tenant_that_has_the_module_passes_while_enforcing()
    {
        var (allowed, denial) = await EvaluateAsync(
            "campaigns.manage",
            enabledModules: ["campaigns", "documents"],
            enforce: true,
            enforcedModules: ["campaigns"]
        );

        Assert.True(allowed);
        Assert.Null(denial);
    }

    [Fact]
    public async Task A_tenant_without_projection_is_not_gated()
    {
        // `null` = la proyección de entitlements todavía no llegó. Denegar acá convertiría la
        // consistencia eventual en 403 para un tenant al día.
        var (allowed, denial) = await EvaluateAsync(
            "campaigns.manage",
            enabledModules: null,
            enforce: true,
            enforcedModules: ["campaigns"]
        );

        Assert.True(allowed);
        Assert.Null(denial);
    }

    [Fact]
    public async Task An_empty_module_list_IS_gated_while_enforcing()
    {
        // `[]` = se sabe que no tiene ningún módulo (suscripción vencida). Es la distinción que, mal
        // resuelta, le daría acceso COMPLETO a un tenant que no paga.
        var (allowed, denial) = await EvaluateAsync(
            "campaigns.manage",
            enabledModules: [],
            enforce: true,
            enforcedModules: ["campaigns"]
        );

        Assert.False(allowed);
        Assert.NotNull(denial);
    }

    [Fact]
    public async Task An_exempt_permission_passes_while_its_module_is_enforced()
    {
        // A5.2 — `communication.notification.read` cae bajo el prefijo `communication.` pero está
        // exento: con `comms` denegando, la campanita del portal sigue funcionando.
        var (allowed, denial) = await EvaluateAsync(
            CommunicationPermissions.NotificationRead,
            enabledModules: ["documents"],
            enforce: true,
            enforcedModules: ["comms"]
        );

        Assert.True(allowed);
        Assert.Null(denial);

        // Y el chat del mismo módulo sí se deniega, para que quede claro que la exención es del
        // permiso y no del módulo entero.
        var (chatAllowed, chatDenial) = await EvaluateAsync(
            "communication.chat.start",
            enabledModules: ["documents"],
            enforce: true,
            enforcedModules: ["comms"]
        );

        Assert.False(chatAllowed);
        Assert.NotNull(chatDenial);
    }

    [Fact]
    public async Task A_transversal_permission_never_reaches_the_gate()
    {
        // Sin módulo asignado no hay nada que exigir: `billing.view` no puede dar 403 de módulo ni con
        // el flag encendido para todo.
        var (allowed, denial) = await EvaluateAsync("billing.view", enabledModules: [], enforce: true);

        Assert.True(allowed);
        Assert.Null(denial);
    }

    [Fact]
    public async Task A_denied_permission_does_not_leak_the_module_state()
    {
        // Orden del pipeline: el módulo se evalúa DESPUÉS del permiso. Un 403 de permiso nunca debe
        // convertirse en `Authz.ModuleUnavailable`, que le contaría al intruso qué incluye el plan.
        var (allowed, denial) = await EvaluateAsync(
            "campaigns.manage",
            enabledModules: [],
            enforce: true,
            enforcedModules: ["campaigns"],
            grantPermission: false
        );

        Assert.False(allowed);
        Assert.Null(denial);
    }

    private sealed class FixedPermissionsSource(bool allowed) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(allowed);
    }

    /// <summary>Lo único que se sustituye: la lectura de la proyección local.</summary>
    private sealed class FixedModulesReader(IReadOnlyList<string>? enabledModules) : ITenantEntitlementModulesReader
    {
        public Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(enabledModules);
    }
}
