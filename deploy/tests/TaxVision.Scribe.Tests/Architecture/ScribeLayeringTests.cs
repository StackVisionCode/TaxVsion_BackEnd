// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Scribe.Application;
using TaxVision.Scribe.Domain.EventMappings;

namespace TaxVision.Scribe.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class ScribeLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(EventTemplateMapping).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
