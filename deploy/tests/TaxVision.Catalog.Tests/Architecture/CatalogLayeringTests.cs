// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Catalog.Application;
using TaxVision.Catalog.Domain;

namespace TaxVision.Catalog.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class CatalogLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(CatalogErrors).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
