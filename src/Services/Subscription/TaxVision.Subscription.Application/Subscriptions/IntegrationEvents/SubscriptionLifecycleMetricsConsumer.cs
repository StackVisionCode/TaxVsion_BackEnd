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
    private static readonly HashSet<string> NonTransitionReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        // A6 — anti-entropía re-anuncia el estado actual.
        nameof(SubscriptionChangeReason.Reconciliation),
        // Cobro recurrente / cambio de preferencia / mandate no mueven el estado base.
        nameof(SubscriptionChangeReason.RecurringChargeSucceeded),
        nameof(SubscriptionChangeReason.RecurringChargeFailed),
        nameof(SubscriptionChangeReason.BillingModeChanged),
        nameof(SubscriptionChangeReason.MandateCreated),
    };

    public static Task Handle(TenantSubscriptionStatusChangedIntegrationEvent evt, ISubscriptionMetrics metrics)
    {
        // Contar estos motivos inventaría movimiento del ciclo de vida que no ocurrió y ensuciaría
        // el tablero: todos ellos viajan por este canal solo para reusar la cadena de envío.
        if (NonTransitionReasons.Contains(evt.Reason))
            return Task.CompletedTask;

        metrics.RecordStatusTransition(evt.PreviousStatus, evt.Status, evt.Reason);
        return Task.CompletedTask;
    }
}
