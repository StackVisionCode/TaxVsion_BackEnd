extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using TaxVision.Subscription.Application.Subscriptions.DomainEvents;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Tests.Architecture;

// Con UseTaxVisionDomainEvents() todo evento de dominio de Subscription va a la cola domain-events: cada uno
// necesita su handler.
public sealed class DomainEventHandlerConventionTests : DomainEventHandlerConventionRules
{
    protected override IEnumerable<Assembly> DomainAssemblies => [typeof(TenantSubscription).Assembly];

    protected override IEnumerable<Assembly> HandlerAssemblies =>
        [typeof(SubscriptionStatusChangedDomainEventHandler).Assembly];
}
