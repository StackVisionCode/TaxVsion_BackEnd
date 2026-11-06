using System.Reflection;
using BuildingBlocks.Authorization;
using BuildingBlocks.Imaging;
using BuildingBlocks.Messaging;
using TaxVision.BuildingBlocks.Testing.Architecture;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>BB-6. La base detecta dependencias prohibidas y respeta las exenciones declaradas.</summary>
public sealed class LayeringRulesBaseTests
{
    [Fact]
    public void FindViolations_ReportsForbiddenDependency()
    {
        // Messaging usa Wolverine: como "Domain" tiene que fallar.
        var violations = LayeringRules.FindViolations(typeof(IIntegrationEvent).Assembly, LayeringRules.Wolverine);

        Assert.Contains(typeof(WolverineFailurePolicies).FullName!, violations);
    }

    [Fact]
    public void FindViolations_SkipsExemptions()
    {
        var assembly = typeof(IIntegrationEvent).Assembly;
        var exemptions = LayeringRules
            .FindViolations(assembly, LayeringRules.Wolverine)
            .ToDictionary(n => n, _ => "prueba");

        Assert.Empty(LayeringRules.FindViolations(assembly, LayeringRules.Wolverine, exemptions));
    }

    [Theory]
    [InlineData("TaxVision.Tasks.Application", "TaxVision.Tasks.Infrastructure")]
    [InlineData("TaxVision.Tasks.ClientForms.Application", "TaxVision.Tasks.ClientForms.Infrastructure")]
    public void InfrastructureNamespaceFor_ReplacesApplicationSuffix(string application, string expected)
    {
        Assert.Equal(expected, LayeringRules.InfrastructureNamespaceFor(application));
    }

    [Fact]
    public void InfrastructureNamespaceFor_RequiresApplicationSuffix()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LayeringRules.InfrastructureNamespaceFor("BuildingBlocks.Imaging")
        );
    }
}

/// <summary>
/// Herencia real de la base: sus reglas se descubren y corren aquí. Se usan dos ensamblados de
/// BuildingBlocks sin EF, ASP.NET ni Wolverine solo para probar el mecanismo.
/// </summary>
public sealed class LayeringRulesBaseInheritanceTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(NotesPermissions).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AvatarImageNormalizer).Assembly;

    protected override string InfrastructureNamespace => "BuildingBlocks.Infrastructure";
}
