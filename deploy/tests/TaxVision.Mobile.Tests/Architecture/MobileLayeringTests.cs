extern alias BbTesting;
using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Mobile.Application;
using TaxVision.Mobile.Domain.Vehicles;

namespace TaxVision.Mobile.Tests.Architecture;

public sealed class MobileLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(Vehicle).Assembly;
    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
