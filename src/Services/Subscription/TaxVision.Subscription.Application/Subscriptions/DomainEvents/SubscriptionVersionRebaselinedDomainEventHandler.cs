using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions.Events;

namespace TaxVision.Subscription.Application.Subscriptions.DomainEvents;

/// <summary>La oficina cambió de edición sin cambiar términos: su foto se rehace con la edición nueva (cascada).</summary>
public static class SubscriptionVersionRebaselinedDomainEventHandler
{
    public static RecalculateEntitlementsCommand Handle(SubscriptionVersionRebaselinedDomainEvent rebaselined) =>
        new(rebaselined.TenantId);
}
