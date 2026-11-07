// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Analytics;

namespace TaxVision.Signature.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class SignatureLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(SignatureAnalyticsSealedRequest).Assembly;

    protected override Assembly ApplicationAssembly => typeof(SignatureStorage).Assembly;

    // Regla propia (SIG-11): Domain sin atributos de mapeo de EF.
    [Fact]
    public void Domain_ShouldNotDependOn_DataAnnotationsSchema()
    {
        var violations = LayeringRules.FindViolations(
            DomainAssembly,
            "System.ComponentModel.DataAnnotations.Schema",
            new Dictionary<string, string>()
        );

        Assert.True(violations.Count == 0, "Domain usa DataAnnotations.Schema:\n  " + string.Join("\n  ", violations));
    }
}
