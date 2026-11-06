extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Postmaster.Application.Sending.DomainEvents;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Postmaster va a la cola domain-events.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(SentMessage).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies => [typeof(SentMessageSentDomainEventHandler).Assembly];
}
