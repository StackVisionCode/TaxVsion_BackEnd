extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Correspondence.Application;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Correspondence va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(EmailThread).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(AssemblyMarker).Assembly];
}
