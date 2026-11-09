// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Subscription.Application.AddOns;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class SubscriptionLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(AddOnDefinition).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AddOnAbsorptionService).Assembly;
}
