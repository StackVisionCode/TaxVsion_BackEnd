// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Inventory.Application;
using TaxVision.Inventory.Domain;

namespace TaxVision.Inventory.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class InventoryLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(InventoryErrors).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
