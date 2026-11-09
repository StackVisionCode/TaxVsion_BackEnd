// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.Tests.Architecture;

// Reglas de capas comunes para Client Forms. Su Infrastructure vive dentro de la de Tasks.
public sealed class ClientFormsLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(FormTemplate).Assembly;

    protected override Assembly ApplicationAssembly =>
        typeof(TaxVision.Tasks.ClientForms.Application.AssemblyMarker).Assembly;

    protected override string InfrastructureNamespace => "TaxVision.Tasks.Infrastructure";
}
