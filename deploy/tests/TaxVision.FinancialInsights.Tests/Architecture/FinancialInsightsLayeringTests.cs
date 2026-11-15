// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.FinancialInsights.Application;
using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class FinancialInsightsLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(ImportBatch).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
