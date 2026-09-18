using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Observabilidad del ciclo de vida (Expiración/Dunning, Fase 6): registra cada transición de estado en la
/// métrica <c>subscription.status.transitions_total{from,to,reason}</c>. Consume el mismo
/// <see cref="TenantSubscriptionStatusChangedIntegrationEvent"/> que Subscription publica en cada cambio —
/// un único punto para TODAS las transiciones, sin tocar los ~7 sitios que las disparan. Solo mide (no
/// publica ni muta), así que no crea ciclos.
/// </summary>
public static class SubscriptionLifecycleMetricsConsumer
{
    public static Task Handle(TenantSubscriptionStatusChangedIntegrationEvent evt, ISubscriptionMetrics metrics)
    {
        metrics.RecordStatusTransition(evt.PreviousStatus, evt.Status, evt.Reason);
        return Task.CompletedTask;
    }
}
