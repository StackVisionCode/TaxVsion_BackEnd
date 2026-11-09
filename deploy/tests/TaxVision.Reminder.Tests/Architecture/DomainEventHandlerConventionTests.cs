extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Reminder.Application;

namespace TaxVision.Reminder.Tests.Architecture;

// REM-BB1: Reminder no despacha eventos de dominio; un evento sin handler sería código muerto.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(ReminderAggregate).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(AssemblyMarker).Assembly];
}
