// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Calendar.Application;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class CalendarLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(Appointment).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
