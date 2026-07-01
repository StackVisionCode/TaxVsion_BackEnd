using BuildingBlocks.Domain;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Seat adicional con ciclo de facturación completamente independiente del plan base.
/// BillingAnchorDay = día del mes en que fue comprado (capped a 28).
/// El precio se actualiza en cada renovación con el precio vigente del plan en ese momento.
/// </summary>
public sealed class SeatSubscription : BaseEntity
{
    public Guid SubscriptionId { get; private set; }
    public Guid TenantId { get; private set; }
    public int Quantity { get; private set; }

    /// <summary>
    /// Precio por seat en el período actual.
    /// Se actualiza en cada renovación con el precio vigente del plan.
    /// </summary>
    public Money PricePerSeat { get; private set; } = default!;

    /// <summary>PricePerSeat × Quantity en el período actual.</summary>
    public Money TotalAmount { get; private set; } = default!;

    public BillingPeriod BillingPeriod { get; private set; }

    // ─── CICLO PROPIO ────────────────────────────────────────────────────────
    // Completamente independiente del ciclo de la suscripción base.

    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    /// <summary>Día 1–28 del mes en que se renueva perpetuamente (fijado al comprar).</summary>
    public int BillingAnchorDay { get; private set; }

    public SeatStatus Status { get; private set; }
    public Guid? InvoiceId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    private SeatSubscription() { }

    internal static SeatSubscription Create(
        Guid subscriptionId,
        Guid tenantId,
        int quantity,
        Money pricePerSeat,
        BillingPeriod billingPeriod,
        DateTime purchasedAtUtc)
    {
        var anchorDay = Math.Min(purchasedAtUtc.Day, 28);
        var periodEnd = Subscription.CalculateNextPeriodEnd(purchasedAtUtc, billingPeriod, anchorDay);

        return new SeatSubscription
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            TenantId = tenantId,
            Quantity = quantity,
            PricePerSeat = pricePerSeat,
            TotalAmount = pricePerSeat.Multiply(quantity),
            BillingPeriod = billingPeriod,
            PeriodStartUtc = purchasedAtUtc,
            PeriodEndUtc = periodEnd,
            BillingAnchorDay = anchorDay,
            Status = SeatStatus.PendingPayment,
            CreatedAtUtc = purchasedAtUtc
        };
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CICLO DE VIDA
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Confirma el seat tras recibir el primer pago exitoso.
    /// Solo válido desde PendingPayment.
    /// </summary>
    internal void Confirm(Guid invoiceId)
    {
        Status = SeatStatus.Active;
        InvoiceId = invoiceId;
        ConfirmedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Renueva el seat con el precio vigente del plan al momento de la renovación.
    /// El nuevo precio puede diferir del anterior si el dueño actualizó el plan.
    /// </summary>
    internal void Renew(Guid invoiceId, DateTime newPeriodEnd, Money newPricePerSeat)
    {
        Status = SeatStatus.Active;
        PeriodStartUtc = PeriodEndUtc;
        PeriodEndUtc = newPeriodEnd;
        PricePerSeat = newPricePerSeat;
        TotalAmount = newPricePerSeat.Multiply(Quantity);
        InvoiceId = invoiceId;
    }

    /// <summary>
    /// Reactiva un seat PastDue tras recibir el pago pendiente.
    /// No avanza el período — el período actual ya venció; la renovación
    /// programará el siguiente período normal.
    /// </summary>
    internal void Reactivate(Guid invoiceId)
    {
        Status = SeatStatus.Active;
        InvoiceId = invoiceId;
    }

    /// <summary>
    /// Marca el seat para que no se renueve al final de su período.
    /// Sigue activo hasta PeriodEndUtc. RenewSeat() lo cancela en lugar de renovar.
    /// </summary>
    internal void CancelAtPeriodEnd() =>
        Status = SeatStatus.CancelAtPeriodEnd;

    /// <summary>Marca el seat como PastDue cuando el cobro de renovación falla.</summary>
    internal void MarkPastDue() => Status = SeatStatus.PastDue;

    /// <summary>Cancela el seat definitivamente.</summary>
    internal void Cancel()
    {
        Status = SeatStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
    }
}
