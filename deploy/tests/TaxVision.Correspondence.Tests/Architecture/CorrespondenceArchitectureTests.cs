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

    private static string Describe(TestResult result) =>
        result.FailingTypes is null
            ? "Unknown architecture violation."
            : "Violating types: " + string.Join(", ", result.FailingTypes.Select(t => t.FullName));
}
