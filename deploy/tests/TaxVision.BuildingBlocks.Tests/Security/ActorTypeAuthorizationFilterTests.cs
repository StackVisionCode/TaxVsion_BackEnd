using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Security;

[Collection(TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization.AuthorizationMetricsCollection.Name)]
public sealed class ActorTypeAuthorizationFilterTests
{
    [Fact]
    public void Blocks_when_action_has_no_AllowActorTypes_declared()
    {
        var context = BuildContext(nameof(FakeController.Undeclared), typeof(FakeController), ActorType.TenantEmployee);

        Authorize(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public void Allows_when_actor_type_is_in_the_declared_set()
    {
        var context = BuildContext(nameof(FakeController.StaffOnly), typeof(FakeController), ActorType.TenantAdmin);

        Authorize(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Blocks_when_actor_type_is_not_in_the_declared_set()
    {
        var context = BuildContext(nameof(FakeController.StaffOnly), typeof(FakeController), ActorType.CustomerPortal);

        Authorize(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public void Blocks_when_the_actor_type_claim_is_missing_entirely()
    {
        var context = BuildContext(nameof(FakeController.StaffOnly), typeof(FakeController), actorType: null);

        Authorize(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public void Skips_AllowAnonymous_actions_even_without_a_declared_actor_type()
    {
        var context = BuildContext(nameof(FakeController.Anonymous), typeof(FakeController), actorType: null);

        Authorize(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void PlatformAdmin_always_passes_even_when_not_in_the_declared_set()
    {
        var context = BuildContext(nameof(FakeController.StaffOnly), typeof(FakeController), ActorType.PlatformAdmin);

        Authorize(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Falls_back_to_the_AllowActorTypes_declared_on_the_controller_when_the_method_has_none()
    {
        var context = BuildContext(
            nameof(CustomerOnlyController.Index),
            typeof(CustomerOnlyController),
            ActorType.CustomerPortal
        );

        Authorize(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Skips_AuthorizedByCapabilityToken_actions_even_without_a_declared_actor_type()
    {
        // Caso real: TenantController.Create — el ticket firmado de registro no lleva
        // actor_type (no es una identidad persistente), pero la policy "TenantRegistration"
        // (Capa 3, aplicada por UseAuthorization() antes de que este filtro corra) ya lo
        // autorizó. El atributo exime a la Capa 2, no reemplaza ni relaja la Capa 3.
        var context = BuildContext(
            nameof(FakeController.CapabilityTokenAuthorized),
            typeof(FakeController),
            actorType: null
        );

        Authorize(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public void Falls_back_to_AuthorizedByCapabilityToken_declared_on_the_controller_when_the_method_has_none()
    {
        var context = BuildContext(
            nameof(CapabilityTokenController.Index),
            typeof(CapabilityTokenController),
            actorType: null
        );

        Authorize(context);

        Assert.Null(context.Result);
    }

    // --- Regresión Fase 7.2 (aislamiento cross-actor) — StaffOnly/CustomerOnly ya prueban un
    // sentido cada uno arriba; estos 2 casos cubren el sentido inverso de cada uno, así que
    // ningún actor type queda sin probar cruzando la frontera del otro.

    [Theory]
    [InlineData(ActorType.TenantEmployee)]
    [InlineData(ActorType.TenantAdmin)]
    public void Blocks_staff_actor_types_from_a_CustomerPortal_only_controller(ActorType actorType)
    {
        var context = BuildContext(nameof(CustomerOnlyController.Index), typeof(CustomerOnlyController), actorType);

        Authorize(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    // Service (M2M, client_credentials) es un actor type real (ver ActorType.cs) usado por
    // endpoints como Subscription.UserAccessController o Scribe.RenderController — a diferencia de
    // PlatformAdmin, NO tiene bypass implícito: si un endpoint no lo declara, un client de servicio
    // queda bloqueado igual que cualquier otro actor type no declarado.

    [Fact]
    public void Blocks_Service_actor_from_a_staff_only_action_that_does_not_declare_it()
    {
        var context = BuildContext(nameof(FakeController.StaffOnly), typeof(FakeController), ActorType.Service);

        Authorize(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Theory]
    [InlineData(ActorType.TenantEmployee)]
    [InlineData(ActorType.TenantAdmin)]
    [InlineData(ActorType.CustomerPortal)]
    public void Blocks_human_actor_types_from_a_Service_only_action(ActorType actorType)
    {
        var context = BuildContext(nameof(FakeController.ServiceOnly), typeof(FakeController), actorType);

        Authorize(context);

        Assert.IsType<ForbidResult>(context.Result);
    }

    [Fact]
    public void Allows_Service_actor_when_explicitly_declared()
    {
        var context = BuildContext(nameof(FakeController.ServiceOnly), typeof(FakeController), ActorType.Service);

        Authorize(context);

        Assert.Null(context.Result);
    }

    private static void Authorize(AuthorizationFilterContext context) =>
        new ActorTypeAuthorizationFilter().OnAuthorization(context);

    private static AuthorizationFilterContext BuildContext(string methodName, Type controllerType, ActorType? actorType)
    {
        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = controllerType.GetMethod(methodName)!,
            ControllerTypeInfo = controllerType.GetTypeInfo(),
        };

        // RBAC Fase 10: ActorTypeAuthorizationFilter ahora resuelve AuthorizationMetrics vía
        // RequestServices para el counter authz.decision — sin este provider mínimo,
        // RequestServices queda null (DefaultHttpContext no lo setea por su cuenta).
        var services = new ServiceCollection().AddSingleton<AuthorizationMetrics>().BuildServiceProvider();
        var httpContext = new DefaultHttpContext { User = BuildPrincipal(actorType), RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        return new AuthorizationFilterContext(actionContext, []);
    }

    private static ClaimsPrincipal BuildPrincipal(ActorType? actorType)
    {
        if (actorType is null)
            return new ClaimsPrincipal(new ClaimsIdentity());

        return new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimNames.ActorType, actorType.Value.ToString())],
                authenticationType: "Test"
            )
        );
    }

    private sealed class FakeController
    {
        [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin)]
        public void StaffOnly() { }

        [AllowActorTypes(ActorType.Service)]
        public void ServiceOnly() { }

        public void Undeclared() { }

        [AllowAnonymous]
        public void Anonymous() { }

        [AuthorizedByCapabilityToken]
        public void CapabilityTokenAuthorized() { }
    }

    [AllowActorTypes(ActorType.CustomerPortal)]
    private sealed class CustomerOnlyController
    {
        public void Index() { }
    }

    [AuthorizedByCapabilityToken]
    private sealed class CapabilityTokenController
    {
        public void Index() { }
    }
}
