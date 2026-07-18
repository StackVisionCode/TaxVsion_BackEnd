using NetArchTest.Rules;
using TaxVision.Scribe.Domain;

namespace TaxVision.Scribe.Tests.Architecture;

/// <summary>
/// Fase 10 — fitness test de las fronteras de Clean Architecture: Domain no depende de nada de
/// afuera, Application no depende de Infrastructure/Api, Infrastructure no depende de Api. Un
/// solo tipo del ensamblado alcanza para resolverlo (<c>Types.InAssembly</c> escanea el ensamblado
/// completo del tipo dado).
/// </summary>
public sealed class ScribeArchitectureTests
{
    private static readonly System.Reflection.Assembly DomainAssembly = typeof(TemplateScope).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAssembly =
        typeof(TaxVision.Scribe.Application.Rendering.RenderEmailQuery).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAssembly =
        typeof(TaxVision.Scribe.Infrastructure.Persistence.ScribeDbContext).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_application()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Scribe.Application")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_infrastructure()
    {
        var result = Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Scribe.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_api()
    {
        var result = Types.InAssembly(DomainAssembly).Should().NotHaveDependencyOn("TaxVision.Scribe.Api").GetResult();

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
            .NotHaveDependencyOn("TaxVision.Scribe.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Scribe.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_should_not_depend_on_api()
    {
        var result = Types
            .InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("TaxVision.Scribe.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        result.FailingTypes is null
            ? "Unknown architecture violation."
            : "Violating types: " + string.Join(", ", result.FailingTypes.Select(t => t.FullName));
}
