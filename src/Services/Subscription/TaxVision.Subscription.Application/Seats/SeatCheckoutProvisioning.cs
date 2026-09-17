using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Seats;

/// <summary>
/// Aprovisionamiento de los asientos de una <see cref="SeatPurchaseIntent"/> ya pagada — crea + activa
/// (co-terminados a la base) SIN volver a cobrar. Extraído para que lo compartan el consumer del webhook
/// (<c>SeatsCheckoutPaidConsumer</c>) y el job de reconciliación (<c>SeatCheckoutReconciliationJob</c>): la
/// misma transición idempotente independientemente de si el pago se confirmó por el evento del proveedor o
/// por el cruce Pending↔Succeeded del reconcile. NO hace SaveChanges ni recalcula entitlements — eso lo
/// decide el caller (distinto stamp de tenant en el bus según venga de un consumer o de un job).
/// </summary>
public static class SeatCheckoutProvisioning
{
    /// <summary>Idempotente: devuelve <c>false</c> si la intención no está en un estado aprovisionable
    /// (ya <c>Provisioned</c>, o no se pudo marcar pagada) — el caller no guarda ni recalcula en ese caso.</summary>
    public static async Task<bool> TryProvisionAsync(
        SeatPurchaseIntent intent,
        TenantSubscription subscription,
        ISubscriptionSeatRepository seats,
        ISubscriptionAuditLogWriter audit,
        ISubscriptionMetrics metrics,
        string correlationId,
        string provisioningMethod,
        DateTime paidAtUtc,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (intent.Status == SeatPurchaseIntentStatus.Provisioned)
            return false;

        var paid = intent.MarkPaid(paidAtUtc);
        if (paid.IsFailure)
            return false;

        await ProvisionSeatsAsync(intent, subscription, seats, nowUtc, ct);
        intent.MarkProvisioned(nowUtc);

        metrics.RecordSeatsPurchased(intent.SeatType.ToString(), intent.Quantity);
        metrics.RecordSeatsBilled(intent.SeatType.ToString(), intent.ProratedTotalCents);
        await AuditEntryFactory.AppendAsync(
            audit,
            intent.TenantId,
            nameof(SeatPurchaseIntent),
            intent.Id,
            "Seats.Purchased",
            intent.RequestedByUserId,
            correlationId,
            before: (object?)null,
            after: new
            {
                SeatType = intent.SeatType.ToString(),
                intent.Quantity,
                BilledCents = intent.ProratedTotalCents,
                Method = provisioningMethod,
            },
            reason: null,
            nowUtc,
            ct
        );
        return true;
    }

    // Crea + activa los asientos ya pagados (co-terminados a la base), sin publicar ningún intent de cobro.
    // Si el período de la base ya venció durante el checkout, co-termina a un ciclo fresco desde ahora.
    private static async Task ProvisionSeatsAsync(
        SeatPurchaseIntent intent,
        TenantSubscription subscription,
        ISubscriptionSeatRepository seats,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var periodEndUtc =
            subscription.CurrentPeriodEndUtc > nowUtc
                ? subscription.CurrentPeriodEndUtc
                : intent.BillingCycle.CalculateNext(nowUtc);

        for (var i = 0; i < intent.Quantity; i++)
        {
            var seat = SubscriptionSeat
                .Purchase(
                    intent.TenantId,
                    intent.SeatType,
                    SeatSourceType.Plan,
                    subscription.PlanId,
                    intent.UnitPrice,
                    intent.BillingCycle,
                    intent.AutoRenew,
                    intent.RequestedByUserId,
                    nowUtc
                )
                .Value;
            seat.Activate(nowUtc, periodEndUtc, intent.RequestedByUserId, nowUtc);
            await seats.AddAsync(seat, ct);
        }
    }
}
