using System.Reflection;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;
using Xunit;

namespace TaxVision.Billing.Tests.Architecture;

/// <summary>
/// H-01 — Billing tenía JWT pero ninguna capa de autorización: los 7 endpoints solo exigían "estar
/// autenticado", así que un CustomerPortal del propio tenant podía listar y emitir facturas. Estas
/// dos fitness functions impiden que la superficie vuelva a quedar así: si mañana alguien agrega un
/// controller o una acción sin Capa 1 ([AllowActorTypes]) o sin Capa 2 ([HasPermission]), falla el
/// build en vez de exponerse en silencio.
/// </summary>
public sealed class BillingAuthorizationArchitectureTests
{
    private static readonly Assembly ApiAssembly =
        typeof(TaxVision.Billing.Api.Controllers.InvoicesController).Assembly;

    [Fact]
    public void Todo_controller_declara_AllowActorTypes()
    {
        var violations = ControllerTypes()
            .Where(type => type.GetCustomAttribute<AllowActorTypesAttribute>(inherit: true) is null)
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(violations.Count == 0, "Controllers sin [AllowActorTypes]: " + string.Join(", ", violations));
    }

    [Fact]
    public void Toda_accion_con_verbo_HTTP_declara_HasPermission()
    {
        var violations = new List<string>();
        foreach (var controllerType in ControllerTypes().Where(type => !IsServiceOnly(type)))
        {
            var classPermission = controllerType.GetCustomAttribute<HasPermissionAttribute>(inherit: true);

            var actions = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null)
                .Where(method => method.GetCustomAttributes().OfType<HttpMethodAttribute>().Any());

            foreach (var action in actions)
            {
                if (action.GetCustomAttribute<HasPermissionAttribute>() is null && classPermission is null)
                    violations.Add($"{controllerType.FullName}.{action.Name}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Acciones sin [HasPermission] (nivel método o controller): " + string.Join(", ", violations)
        );
    }

    [Fact]
    public void Los_controllers_internos_solo_admiten_tokens_de_servicio()
    {
        var violations = ControllerTypes()
            .Where(type => type.GetCustomAttribute<RouteAttribute>()?.Template.StartsWith("internal/") == true)
            .Where(type =>
                !IsServiceOnly(type)
                || type.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()?.Policy
                    != "ServiceOnly"
            )
            .Select(type => type.FullName!)
            .ToList();

        Assert.True(violations.Count == 0, "Controllers internos sin ServiceOnly: " + string.Join(", ", violations));
    }

    // Un controller M2M (solo actor Service) no tiene usuario con permisos: lo cubre la policy ServiceOnly.
    private static bool IsServiceOnly(Type type) =>
        type.GetCustomAttribute<AllowActorTypesAttribute>(inherit: true)?.ActorTypes is { Count: 1 } actorTypes
        && actorTypes.Contains(BuildingBlocks.ActorTypeAuthorization.ActorType.Service);

    private static IEnumerable<Type> ControllerTypes() =>
        Types.InAssembly(ApiAssembly).That().Inherit(typeof(ControllerBase)).And().AreClasses().GetTypes();
}
