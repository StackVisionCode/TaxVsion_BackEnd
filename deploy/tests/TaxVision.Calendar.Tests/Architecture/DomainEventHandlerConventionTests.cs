extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Calendar.Application;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Calendar va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(Appointment).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(AssemblyMarker).Assembly];
}
