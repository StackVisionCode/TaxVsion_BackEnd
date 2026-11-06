// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Connectors.Application;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class ConnectorsLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(ImapCredentials).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
