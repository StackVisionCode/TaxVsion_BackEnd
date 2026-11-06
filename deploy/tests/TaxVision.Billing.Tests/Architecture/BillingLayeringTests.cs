// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class BillingLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(Invoice).Assembly;

    protected override Assembly ApplicationAssembly => typeof(BillingVisibilityOptions).Assembly;
}
