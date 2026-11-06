// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Sms.Application;
using TaxVision.Sms.Domain.Consent;

namespace TaxVision.Sms.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class SmsLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(SmsKeywordParser).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
