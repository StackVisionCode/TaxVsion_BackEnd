using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Tests.Architecture;

/// <summary>
/// Fase 16 — fitness tests de las fronteras de Clean Architecture, mismo criterio que
/// <c>ConnectorsArchitectureTests</c>/<c>ScribeArchitectureTests</c>: Domain no depende de nada de
/// afuera, Application no depende de Infrastructure/Api, Infrastructure no depende de Api. Además
/// dos reglas propias de este servicio (plan §36 Fase 16): solo <c>PostmasterClient</c> le habla a
/// Postmaster directo, y ningún tipo de Correspondence conoce Scribe (nunca lo necesitó — ver el
/// test de abajo, que confirma en vez de asumir).
/// </summary>
public sealed class CorrespondenceArchitectureTests
{
    private static readonly System.Reflection.Assembly DomainAssembly = typeof(Draft).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAssembly =
        typeof(TaxVision.Correspondence.Application.Compose.SendDraftCommand).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAssembly =
        typeof(TaxVision.Correspondence.Infrastructure.Persistence.CorrespondenceDbContext).Assembly;
    private static readonly System.Reflection.Assembly ApiAssembly =
        typeof(TaxVision.Correspondence.Api.Controllers.DraftsController).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_application()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Correspondence.Application")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Correspondence.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Correspondence.Api")
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
    public void Domain_should_not_depend_on_wolverine()
    {
        var result = Types.InAssembly(DomainAssembly).Should().NotHaveDependencyOn("Wolverine").GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_http_client()
    {
        var result = Types.InAssembly(DomainAssembly).Should().NotHaveDependencyOn("System.Net.Http").GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_mailkit()
    {
        var result = Types.InAssembly(DomainAssembly).Should().NotHaveDependencyOn("MailKit").GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_minio()
    {
        var result = Types.InAssembly(DomainAssembly).Should().NotHaveDependencyOn("Minio").GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Correspondence.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Correspondence.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Correspondence.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// Fase 2 (plan de hardening §1) — los controllers deben orquestar solo a través de Application
    /// (comandos/queries/abstracciones), nunca hablarle a Infrastructure directo. Es la misma
    /// frontera de Clean Architecture que ya protegen
    /// <see cref="Application_should_not_depend_on_infrastructure"/> e
    /// <see cref="Infrastructure_should_not_depend_on_api"/>, pero desde el lado que faltaba: hoy
    /// ningún controller viola esto (solo usan tipos de Application + <c>IMessageBus</c> de
    /// Wolverine), y este test evita que empiece a pasar mañana sin que nadie lo note.
    /// </summary>
    [Fact]
    public void Controllers_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("TaxVision.Correspondence.Api.Controllers")
            .Should()
            .NotHaveDependencyOn("TaxVision.Correspondence.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// Solo <c>PostmasterClient</c> (Infrastructure/Postmaster) puede depender del tipo concreto
    /// <c>PostmasterClient</c> — todo lo demás (incluida la composition root
    /// <c>DependencyInjection.cs</c>, excluida a propósito porque registrar el tipo concreto en DI
    /// es su trabajo) debe hablarle a Postmaster solo a través de <c>IPostmasterClient</c>
    /// (Application.Abstractions). Ningún tipo de Application/Api puede violar esto tampoco — ya lo
    /// cubre <see cref="Application_should_not_depend_on_infrastructure"/> arriba, pero este test
    /// deja explícito el "por qué" (la abstracción, no el cliente HTTP concreto).
    /// </summary>
    [Fact]
    public void Only_PostmasterClient_should_reference_the_concrete_postmaster_http_client()
    {
        var result = Types
            .InAssembly(InfrastructureAssembly)
            .That()
            .DoNotResideInNamespace("TaxVision.Correspondence.Infrastructure.Postmaster")
            .And()
            .DoNotHaveName(["DependencyInjection"])
            .Should()
            .NotHaveDependencyOn("TaxVision.Correspondence.Infrastructure.Postmaster.PostmasterClient")
            .GetResult();
        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// Correspondence nunca necesitó a Scribe (a diferencia de Postmaster/Notification/Signature,
    /// que sí renderizan vía Scribe) — Postmaster ya recibe el HTML final armado por el usuario en
    /// el composer, nadie en Correspondence renderiza nada. Este test CONFIRMA esa premisa en vez
    /// de asumirla (plan §36 Fase 16, punto explícito).
    /// </summary>
    [Fact]
    public void No_type_should_reference_scribe()
    {
        foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly })
        {
            var result = Types.InAssembly(assembly).Should().NotHaveDependencyOn("TaxVision.Scribe").GetResult();
            Assert.True(result.IsSuccessful, Describe(result));
        }
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
