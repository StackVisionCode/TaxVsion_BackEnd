using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;

namespace TaxVision.Notes.Tests.Architecture;

/// <summary>
/// Fase 9 (03_Plan_De_Fases.md) — fitness functions de cierre, mismo patrón que las 14 copias
/// existentes en el resto del monorepo (ver p.ej. CloudStorageActorTypeArchitectureTests): falla el
/// build si un controller nuevo (o una acción nueva) queda sin <see cref="AllowActorTypesAttribute"/>
/// o sin <see cref="RateLimitAttribute"/>/<see cref="RateLimitExemptAttribute"/>. Espeja exactamente
/// la resolución en runtime de <c>ActorTypeAuthorizationFilter.ResolveDeclaredActorTypes</c> (método
/// primero, controller como fallback).
/// </summary>
public sealed class NotesArchitectureTests
{
    private static readonly Assembly ApiAssembly = typeof(TaxVision.Notes.Api.Controllers.NotesController).Assembly;

    [Fact]
    public void Controller_actions_should_declare_AllowActorTypes()
    {
        var violations = FindActionsMissingAllowActorTypes(ApiAssembly);
        Assert.True(
            violations.Count == 0,
            "Actions missing [AllowActorTypes] (method or controller level): " + string.Join(", ", violations)
        );
    }

    private static List<string> FindActionsMissingAllowActorTypes(Assembly apiAssembly)
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
            var classIsAnonymous =
                controllerType.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;
            var classIsCapabilityToken =
                controllerType.GetCustomAttribute<AuthorizedByCapabilityTokenAttribute>(inherit: true) is not null;
            var classAllowActorTypes = controllerType.GetCustomAttribute<AllowActorTypesAttribute>(inherit: true);

            var actions = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);

            foreach (var action in actions)
            {
                if (classIsAnonymous || action.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                    continue;
                if (
                    classIsCapabilityToken
                    || action.GetCustomAttribute<AuthorizedByCapabilityTokenAttribute>() is not null
                )
                    continue;

                var allowActorTypes = action.GetCustomAttribute<AllowActorTypesAttribute>() ?? classAllowActorTypes;
                if (allowActorTypes is null)
                    violations.Add($"{controllerType.FullName}.{action.Name}");
            }
        }

        return violations;
    }

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

    /// <summary>
    /// Fase 9 §3 (aislamiento por capas) — Controllers solo pueden depender de Application (nunca
    /// directo de Domain concreto salvo enums/VOs de request binding, ni de Infrastructure). Mismo
    /// criterio que <c>CorrespondenceArchitectureTests</c>.
    /// </summary>
    [Fact]
    public void Controllers_should_not_depend_on_Infrastructure()
    {
        var result = Types
            .InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("TaxVision.Notes.Api.Controllers")
            .ShouldNot()
            .HaveDependencyOn("TaxVision.Notes.Infrastructure")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "Controllers must not depend on Infrastructure directly: "
                + string.Join(", ", result.FailingTypeNames ?? [])
        );
    }
}
