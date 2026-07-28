using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace TaxVision.Documents.Tests.Architecture;

/// <summary>Reglas de arquitectura (guardrail #39): las dependencias entre capas se verifican
/// automáticamente. Documents NUNCA referencia Scribe ni Notification en su flujo.</summary>
public sealed class DocumentsArchitectureTests
{
    private static readonly Assembly Domain = typeof(TaxVision.Documents.Domain.Generations.DocumentGeneration).Assembly;
    private static readonly Assembly Application = typeof(TaxVision.Documents.Application.Abstractions.IDocumentGenerationRepository).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_Infrastructure_or_EF_or_Wolverine()
    {
        var result = Types.InAssembly(Domain)
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
        var result = Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny("TaxVision.Documents.Api", "TaxVision.Documents.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void Application_should_not_depend_on_Scribe_or_Notification()
    {
        var result = Types.InAssembly(Application)
            .Should()
            .NotHaveDependencyOnAny("TaxVision.Scribe", "TaxVision.Notification")
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    private static string Fail(TestResult result) =>
        "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
