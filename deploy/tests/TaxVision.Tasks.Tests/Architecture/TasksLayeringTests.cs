// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Tasks.Application;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Tests.Architecture;

// Reglas de capas comunes (LayeringRulesBase) para Tasks.
public sealed class TasksLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(TaskItem).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
