using System.Reflection;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;
using Xunit;

namespace TaxVision.Documents.Tests.Architecture;

/// <summary>Reglas de arquitectura (guardrail #39): las dependencias entre capas se verifican
/// automáticamente. Documents NUNCA referencia Scribe ni Notification en su flujo.</summary>
public sealed class DocumentsArchitectureTests
{
    private static readonly Assembly Domain =
        typeof(TaxVision.Documents.Domain.Generations.DocumentGeneration).Assembly;
    private static readonly Assembly Application =
        typeof(TaxVision.Documents.Application.Abstractions.IDocumentGenerationRepository).Assembly;
    private static readonly Assembly ApiAssembly =
        typeof(TaxVision.Documents.Api.Controllers.InternalDocumentBrandingController).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_Infrastructure_or_EF_or_Wolverine()
    {
        var result = Types
            .InAssembly(Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "TaxVision.Documents.Infrastructure",
                "Microsoft.EntityFrameworkCore",
                "Wolverine",
                "Minio",
                "Microsoft.Playwright"
            )
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void Application_should_not_depend_on_Api_or_Infrastructure()
    {
        var result = Types
            .InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny("TaxVision.Documents.Api", "TaxVision.Documents.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void Application_should_not_depend_on_Scribe_or_Notification()
    {
        var result = Types
            .InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny("TaxVision.Scribe", "TaxVision.Notification")
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    /// <summary>
    /// Fase 9 del plan de RateLimit (Plan_Implementacion_Fases.md §3.10) — fitness function que
    /// falla el build si una acción pública con verbo HTTP (<see cref="HttpMethodAttribute"/>, base
    /// de <c>HttpGetAttribute</c>/<c>HttpPostAttribute</c>/etc.) queda sin
    /// <see cref="RateLimitAttribute"/> ni <see cref="RateLimitExemptAttribute"/>, sea a nivel de
    /// acción o heredado del controller. Documents solo expone controllers M2M internos
    /// (<c>Internal*Controller</c>, namespace <c>TaxVision.Documents.Api.Controllers</c>) — no hay
    /// controllers públicos de cara al Gateway, pero la regla aplica igual: todo verbo HTTP necesita
    /// una decisión explícita.
    /// </summary>
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

    private static string Fail(TestResult result) =>
        "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
