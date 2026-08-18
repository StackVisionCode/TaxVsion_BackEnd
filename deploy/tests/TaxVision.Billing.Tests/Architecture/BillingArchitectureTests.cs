using System.Reflection;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;
using Xunit;

namespace TaxVision.Billing.Tests.Architecture;

/// <summary>
/// Fase 9 del plan de RateLimit (Plan_Implementacion_Fases.md §3.10) — fitness function que falla
/// el build si una acción pública con verbo HTTP (<see cref="HttpMethodAttribute"/>, base de
/// <c>HttpGetAttribute</c>/<c>HttpPostAttribute</c>/etc.) queda sin <see cref="RateLimitAttribute"/>
/// ni <see cref="RateLimitExemptAttribute"/>, sea a nivel de acción o heredado del controller.
/// Mismo patrón de reflexión que <c>CustomerActorTypeArchitectureTests</c> (que hace el chequeo
/// equivalente para <c>AllowActorTypesAttribute</c>) — Billing no tenía todavía ninguna red de
/// seguridad arquitectónica automatizada, este es el primer test de este tipo para el servicio.
/// </summary>
public sealed class BillingArchitectureTests
{
    private static readonly Assembly ApiAssembly =
        typeof(TaxVision.Billing.Api.Controllers.InvoicesController).Assembly;

    [Fact]
    public void Controller_actions_should_declare_RateLimit_or_RateLimitExempt()
    {
        var violations = FindActionsMissingRateLimit(ApiAssembly);
        Assert.True(
            violations.Count == 0,
            "Actions missing [RateLimit] or [RateLimitExempt] (method or controller level): "
                + string.Join(", ", violations)
        );
    }

    private static List<string> FindActionsMissingRateLimit(Assembly apiAssembly)
    {
        var controllerTypes = Types
            .InAssembly(apiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .And()
            .AreClasses()
            .GetTypes();

        var violations = new List<string>();
        foreach (var controllerType in controllerTypes)
        {
            var classRateLimit = controllerType.GetCustomAttribute<RateLimitAttribute>(inherit: true);
            var classRateLimitExempt = controllerType.GetCustomAttribute<RateLimitExemptAttribute>(inherit: true);

            var actions = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);

            foreach (var action in actions)
            {
                var hasHttpVerb = action.GetCustomAttributes().OfType<HttpMethodAttribute>().Any();
                if (!hasHttpVerb)
                    continue;

                var rateLimit = action.GetCustomAttribute<RateLimitAttribute>() ?? classRateLimit;
                var rateLimitExempt = action.GetCustomAttribute<RateLimitExemptAttribute>() ?? classRateLimitExempt;

                if (rateLimit is null && rateLimitExempt is null)
                    violations.Add($"{controllerType.FullName}.{action.Name}");
            }
        }

        return violations;
    }
}
