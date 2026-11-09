// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Codes.Application.Compensations.CompensateRedemption;
using TaxVision.Codes.Domain.Compensations;

namespace TaxVision.Growth.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class CodesLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(CodeCompensation).Assembly;

    protected override Assembly ApplicationAssembly => typeof(CompensateRedemptionHandler).Assembly;

    // El ensamblado no deduce Infrastructure: es compartido por los contextos de Growth.
    protected override string InfrastructureNamespace => "TaxVision.Growth.Infrastructure";

    // GRW-5: Application de Growth tampoco usa Wolverine (regla propia, más estricta que la base).
    [Fact]
    public void Application_ShouldNotDependOn_Wolverine()
    {
        var violations = LayeringRules.FindViolations(ApplicationAssembly, LayeringRules.Wolverine);

        Assert.True(
            violations.Count == 0,
            $"Application ({ApplicationAssembly.GetName().Name}) depende de Wolverine:\n  "
                + string.Join("\n  ", violations)
        );
    }
}
