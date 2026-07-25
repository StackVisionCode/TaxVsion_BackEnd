using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Tests.Architecture;

public sealed class ConnectorsArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(ProviderCode).Assembly;
    private static readonly Assembly ApplicationAssembly =
        typeof(TaxVision.Connectors.Application.Messages.GetMessageBodyQuery).Assembly;
    private static readonly Assembly InfrastructureAssembly =
        typeof(TaxVision.Connectors.Infrastructure.Persistence.ConnectorsDbContext).Assembly;
    private static readonly Assembly ApiAssembly =
        typeof(TaxVision.Connectors.Api.Controllers.AccountsController).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_application()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Connectors.Application")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Connectors.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Connectors.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_entity_framework()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Connectors.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Connectors.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Connectors.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// Fase 6 del plan de autorización por actor type — fitness function que falla el build si un
    /// controller nuevo (o una acción nueva) queda sin <see cref="AllowActorTypesAttribute"/>, sea a
    /// nivel de acción o heredado del controller. Espeja exactamente la resolución que hace
    /// <c>ActorTypeAuthorizationFilter.ResolveDeclaredActorTypes</c> en runtime (método primero,
    /// controller como fallback) y respeta las mismas dos excepciones que el filtro
    /// (<see cref="AllowAnonymousAttribute"/>, <see cref="AuthorizedByCapabilityTokenAttribute"/>).
    /// </summary>
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

    private static string Describe(TestResult result) =>
        result.FailingTypes is null
            ? "Unknown architecture violation."
            : "Violating types: " + string.Join(", ", result.FailingTypes.Select(t => t.FullName));
}
