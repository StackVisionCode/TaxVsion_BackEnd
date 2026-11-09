// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Wallet.Application;
using TaxVision.Wallet.Domain.Permissions;

namespace TaxVision.Wallet.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class WalletLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(RolePermissionsProjection).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
