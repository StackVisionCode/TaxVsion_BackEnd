using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Domain.Plans.Events;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.DomainEvents;

/// <summary>Avisa a los fronts que el catálogo cambió (cascada por el outbox, después del commit).</summary>
public static class PlanWithdrawnFromSaleDomainEventHandler
{
    public static CatalogChangedIntegrationEvent Handle(PlanWithdrawnFromSaleDomainEvent changed, Envelope envelope) =>
        new()
        {
            ChangeKind = "plan.withdrawn_from_sale",
            EntityId = changed.PlanId,
            EntityCode = changed.PlanCode,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
        };
}
