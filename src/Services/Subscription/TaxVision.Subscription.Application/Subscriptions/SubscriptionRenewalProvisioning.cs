using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions;

/// <summary>Resultado de intentar reactivar una suscripción a partir de una intención de renovación pagada.</summary>
/// <param name="Provisioned">La intención quedó en estado <c>Provisioned</c> (o ya lo estaba): éxito idempotente.</param>
/// <param name="StatusChanged">La suscripción transicionó a Active en ESTA llamada (hay que publicar el evento).</param>
/// <param name="PreviousStatus">Estado de la suscripción antes de la reactivación (para el evento).</param>
public readonly record struct RenewalProvisioningOutcome(
    bool Provisioned,
    bool StatusChanged,
    SubscriptionStatus PreviousStatus
);

/// <summary>
/// Aplicación idempotente de un pago de renovación self-service. Dos rutas:
///  - **Lapso** (PastDue/Suspended/Expired): reactiva via <see cref="TenantSubscription.ReactivateAfterSelfServicePayment"/>.
///  - **Prepago** (Trialing/Active): adelanta el próximo ciclo via <see cref="TenantSubscription.BeginRenewal"/> +
///    <see cref="TenantSubscription.CompleteRenewal"/>. El status se mantiene; el período avanza desde
///    <c>CurrentPeriodEndUtc</c> un ciclo más adelante — el user "regala" tiempo extendiendo su acceso.
/// Comparte el consumer del webhook y el job de reconciliación. NO hace <c>SaveChanges</c> ni publica eventos
/// ni recalcula entitlements — eso es del caller.
/// </summary>
public static class SubscriptionRenewalProvisioning
{
    public static RenewalProvisioningOutcome TryReactivate(
        SubscriptionRenewalIntent intent,
        TenantSubscription subscription,
        DateTime paidAtUtc,
        DateTime nowUtc
    )
    {
        if (intent.Status == SubscriptionRenewalIntentStatus.Provisioned)
            return new RenewalProvisioningOutcome(Provisioned: true, StatusChanged: false, subscription.Status);

        var previousStatus = subscription.Status;

        if (intent.MarkPaid(paidAtUtc).IsFailure)
            return new RenewalProvisioningOutcome(Provisioned: false, StatusChanged: false, previousStatus);

        // Ruta prepago: la sub sigue Active/Trialing, el user adelantó el pago del próximo ciclo. Se avanza el
        // período sin tocar el status; el banner y la UI dejan de mostrar "renews in N days".
        if (subscription.Status == SubscriptionStatus.Active || subscription.Status == SubscriptionStatus.Trialing)
        {
            var idempotencyKey = intent.Id.ToString("N");
            var begin = subscription.BeginRenewal(idempotencyKey, intent.RequestedByUserId, nowUtc);
            if (begin.IsFailure)
                return new RenewalProvisioningOutcome(Provisioned: false, StatusChanged: false, previousStatus);

            var renewal = subscription.FindRenewalByIdempotencyKey(idempotencyKey);
            if (renewal is not null)
            {
                var complete = subscription.CompleteRenewal(
                    renewal.Id,
                    intent.SaaSPaymentId?.ToString(),
                    intent.RequestedByUserId,
                    nowUtc
                );
                if (complete.IsFailure)
                    return new RenewalProvisioningOutcome(Provisioned: false, StatusChanged: false, previousStatus);
            }

            intent.MarkProvisioned(nowUtc);
            // Prepago no transiciona el status: el banner lo detecta por el nuevo nextRenewalAtUtc.
            return new RenewalProvisioningOutcome(Provisioned: true, StatusChanged: false, previousStatus);
        }

        // Ruta recuperación: la sub estaba en lapso, el pago la reactiva con un nuevo ciclo desde ahora.
        var periodEndUtc = subscription.BillingCycle.CalculateNext(nowUtc);
        var reactivated = subscription.ReactivateAfterSelfServicePayment(
            nowUtc,
            periodEndUtc,
            intent.RequestedByUserId,
            nowUtc
        );

        intent.MarkProvisioned(nowUtc);
        return new RenewalProvisioningOutcome(Provisioned: true, reactivated.IsSuccess, previousStatus);
    }
}
