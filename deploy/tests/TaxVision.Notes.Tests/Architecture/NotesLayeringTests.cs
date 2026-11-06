// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Notes.Application;
using TaxVision.Notes.Domain.Backfill;

namespace TaxVision.Notes.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class NotesLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(TenantBackfillState).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
