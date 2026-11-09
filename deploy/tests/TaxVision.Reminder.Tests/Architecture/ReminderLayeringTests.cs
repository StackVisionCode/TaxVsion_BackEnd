// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Reminder.Application;
using TaxVision.Reminder.Domain.Permissions;

namespace TaxVision.Reminder.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class ReminderLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(RolePermissionsProjection).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
