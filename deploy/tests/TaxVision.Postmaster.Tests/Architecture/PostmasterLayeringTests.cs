// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Postmaster.Application;
using TaxVision.Postmaster.Domain.Feedback;

namespace TaxVision.Postmaster.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class PostmasterLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(DeliveryFeedbackReceipt).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
