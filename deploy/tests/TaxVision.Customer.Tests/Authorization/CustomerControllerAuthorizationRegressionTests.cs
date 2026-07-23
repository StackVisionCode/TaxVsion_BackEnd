using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaxVision.Customer.Tests.Authorization;

/// <summary>
/// RBAC Fase 1 (RBAC_Hardening_Plan.md) — regresión del bug donde `CustomerController` combinaba
/// <c>[Authorize(Roles = "TenantEmployee,TenantAdmin")]</c> (o <c>"TenantAdmin"</c>) con
/// <c>[AllowActorTypes(..., PlatformAdmin)]</c> en la misma acción. Los dos atributos se ANDean en
/// ASP.NET Core: un PlatformAdmin (cuyo claim de rol es literalmente "PlatformAdmin", no
/// "TenantAdmin" ni "TenantEmployee") quedaba rechazado por el `Roles=` ANTES de que
/// `AllowActorTypes` lo evaluara — aunque este último sí lo declarara explícitamente permitido.
///
/// No usamos WebApplicationFactory (sin precedente en el repo, ver comentario de
/// ServiceOnlyPolicyTests.cs) — en su lugar reflejamos la misma regla que hace tóxica la
/// combinación: si una acción declara PlatformAdmin en su AllowActorTypes efectivo, ningún
/// [Authorize(Roles=...)] en esa acción (o heredado del controller) puede excluirlo.
/// </summary>
public sealed class CustomerControllerAuthorizationRegressionTests
{
    private static readonly Type ControllerType = typeof(TaxVision.Customer.Api.Controllers.CustomerController);

    [Fact]
    public void No_action_declares_a_role_based_Authorize_attribute()
    {
        // Fase 1 elimino por completo [Authorize(Roles = "...")] de CustomerController — el gate
        // real vive en [HasPermission] + [AllowActorTypes]. Si vuelve a aparecer un Roles=, es
        // exactamente el patron que causo el bug original.
        var violations = FindActionsWithRoleBasedAuthorize();
        Assert.True(
            violations.Count == 0,
            "Actions with role-based [Authorize(Roles=...)] (should use [HasPermission] instead): "
                + string.Join(", ", violations)
        );
    }

    [Fact]
    public void PlatformAdmin_is_never_excluded_by_a_role_based_Authorize_attribute()
    {
        // Contrato general (no solo "no hay Roles= hoy"): si mañana alguien reintroduce un
        // [Authorize(Roles=...)] en una accion cuyo AllowActorTypes efectivo incluye
        // PlatformAdmin, ese Roles= NO puede excluir el rol "PlatformAdmin" — es exactamente el
        // bug de Fase 1.
        var violations = FindPlatformAdminExclusions();
        Assert.True(
            violations.Count == 0,
            "Actions where AllowActorTypes permits PlatformAdmin but [Authorize(Roles=...)] excludes "
                + "the \"PlatformAdmin\" role: "
                + string.Join(", ", violations)
        );
    }

    private static List<string> FindActionsWithRoleBasedAuthorize()
    {
        var violations = new List<string>();

        foreach (var action in GetActions(ControllerType))
        {
            var authorize = action.GetCustomAttribute<AuthorizeAttribute>();
            if (authorize?.Roles is { Length: > 0 })
                violations.Add($"{ControllerType.Name}.{action.Name}");
        }

        return violations;
    }

    private static List<string> FindPlatformAdminExclusions()
    {
        var controllerType = ControllerType;
        var classAllowActorTypes = controllerType.GetCustomAttribute<AllowActorTypesAttribute>(inherit: true);
        var violations = new List<string>();

        foreach (var action in GetActions(controllerType))
        {
            var allowActorTypes = action.GetCustomAttribute<AllowActorTypesAttribute>() ?? classAllowActorTypes;
            if (allowActorTypes is null || !allowActorTypes.ActorTypes.Contains(ActorType.PlatformAdmin))
                continue;

            var authorize = action.GetCustomAttribute<AuthorizeAttribute>();
            var roles = authorize?.Roles?.Split(
                ',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
            );
            if (roles is { Length: > 0 } && !roles.Contains("PlatformAdmin"))
                violations.Add($"{controllerType.Name}.{action.Name}");
        }

        return violations;
    }

    private static IEnumerable<MethodInfo> GetActions(Type controllerType) =>
        controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);
}
