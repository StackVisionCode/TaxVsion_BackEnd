using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Domain.Plans.Events;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.DomainEvents;

/// <summary>Avisa a los fronts que el catálogo cambió (cascada por el outbox, después del commit).</summary>
public static class PlanPutOnSaleDomainEventHandler
{
    public static CatalogChangedIntegrationEvent Handle(PlanPutOnSaleDomainEvent changed, Envelope envelope) =>
        new()
        {
            ChangeKind = "plan.put_on_sale",
            EntityId = changed.PlanId,
            EntityCode = changed.PlanCode,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
        };
}
