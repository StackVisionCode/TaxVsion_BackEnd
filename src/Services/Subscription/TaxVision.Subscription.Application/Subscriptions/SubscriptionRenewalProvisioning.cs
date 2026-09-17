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
/// Reactivación idempotente de la suscripción a partir de una <see cref="SubscriptionRenewalIntent"/> pagada,
/// compartida por el consumer del webhook y el job de reconciliación (mismo rol que <c>SeatCheckoutProvisioning</c>
/// para seats). NO hace <c>SaveChanges</c> ni publica eventos ni recalcula entitlements — eso es del caller.
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

        // Si ya está Active (admin reactivó, o redelivery post-reactivación) la transición falla — igual
        // marcamos la intención aprovisionada (el objetivo ya se logró) y no reportamos cambio de estado.
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
