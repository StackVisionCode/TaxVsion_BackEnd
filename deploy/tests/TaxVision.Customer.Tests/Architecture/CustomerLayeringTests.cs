// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Addresses;

namespace TaxVision.Customer.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class CustomerLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(CustomerAddress).Assembly;

    protected override Assembly ApplicationAssembly => typeof(CustomerVisibilityOptions).Assembly;
}
