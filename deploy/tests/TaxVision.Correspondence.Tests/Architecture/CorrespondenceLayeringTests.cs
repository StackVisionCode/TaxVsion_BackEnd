// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Correspondence.Application;
using TaxVision.Correspondence.Domain.Audit;

namespace TaxVision.Correspondence.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class CorrespondenceLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(CorrespondenceAuditLog).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
