// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Auth.Onboarding.Application.Sagas;
using TaxVision.Auth.Onboarding.Domain.TenantOnboardings;

namespace TaxVision.Auth.Tests.Architecture;

// BB-6: las mismas reglas de capas para los ensamblados de Onboarding.
public sealed class OnboardingLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(TenantOnboarding).Assembly;

    protected override Assembly ApplicationAssembly => typeof(TenantOnboardingProcessManager).Assembly;
}
