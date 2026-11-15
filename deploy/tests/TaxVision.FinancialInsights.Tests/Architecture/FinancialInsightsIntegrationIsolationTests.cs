extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;

namespace TaxVision.FinancialInsights.Tests.Architecture;

// BB-10 sobre las pruebas de este servicio: ningún host ni base de pruebas deja algo para la flota.
public sealed class FinancialInsightsIntegrationIsolationTests
{
    [Fact]
    public void Tests_leave_nothing_for_the_fleet()
    {
        var findings = IntegrationIsolationRules
            .Scan("deploy/tests/TaxVision.FinancialInsights.Tests")
            .Select(f => f.Key)
            .ToArray();

        Assert.True(findings.Length == 0, "Pruebas sin aislar:\n  " + string.Join("\n  ", findings));
    }
}
