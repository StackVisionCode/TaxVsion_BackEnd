using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Re-anuncio del estado ACTUAL por el canal de <see cref="TenantSubscriptionStatusChangedIntegrationEvent"/>, sin
/// transición: la anti-entropía (<see cref="SubscriptionChangeReason.Reconciliation"/>) y el recordatorio de fin de
/// acceso (<see cref="SubscriptionChangeReason.AccessEnding"/>). Las transiciones reales las anuncia el evento de
/// dominio <c>SubscriptionStatusChangedDomainEvent</c>; no se publican a mano.
/// </summary>
public static class SubscriptionStatusChangedPublishing
{
    public static TenantSubscriptionStatusChangedIntegrationEvent Reannouncement(
        TenantSubscription subscription,
        SubscriptionChangeReason reason,
        string? correlationId = null
    ) =>
        new()
        {
            TenantId = subscription.TenantId,
            TenantSubscriptionId = subscription.Id,
            Status = subscription.Status.ToString(),
            // Sin transición: el anterior es el actual. Los consumidores miran Status.
            PreviousStatus = subscription.Status.ToString(),
            Reason = reason.ToString(),
            // La cancelación programada necesita decir hasta cuándo llega el acceso ya pagado.
            AccessEndsAtUtc = subscription.CancelAtPeriodEnd ? subscription.CurrentPeriodEndUtc : null,
            CorrelationId = correlationId ?? string.Empty,
        };
}
