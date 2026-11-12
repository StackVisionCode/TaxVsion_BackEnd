using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions.Events;

namespace TaxVision.Subscription.Application.Subscriptions.DomainEvents;

/// <summary>D11: la oficina pasó a otra edición; su foto de derechos se rehace con la nueva (cascada).</summary>
public static class SubscriptionVersionChangedDomainEventHandler
{
    public static RecalculateEntitlementsCommand Handle(SubscriptionVersionChangedDomainEvent changed) =>
        new(changed.TenantId);
}
