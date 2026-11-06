// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;

namespace TaxVision.CloudStorage.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class CloudStorageLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(StorageAccessLog).Assembly;

    protected override Assembly ApplicationAssembly => typeof(CloudStoragePerItemAccessOptions).Assembly;
}
