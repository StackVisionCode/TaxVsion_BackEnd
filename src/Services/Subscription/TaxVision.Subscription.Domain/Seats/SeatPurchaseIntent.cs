using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Seats;

public enum SeatPurchaseIntentStatus
{
    /// <summary>Creado; se emitió (o se está emitiendo) la sesión de checkout, esperando el pago del cliente.</summary>
    Pending,

    /// <summary>Pago confirmado por el proveedor (webhook/reconcile); pendiente de aprovisionar los asientos.</summary>
    Paid,

    /// <summary>Asientos creados y activados a partir de esta intención.</summary>
    Provisioned,

    /// <summary>El pago falló o la sesión expiró sin pagar.</summary>
    Failed,
}

/// <summary>
/// Intención de compra de asientos por HOSTED-CHECKOUT (redirect), para el tenant que NO tiene método de
/// pago en archivo. Es el equivalente para seats del <c>TenantOnboarding</c>: Subscription (dueño de los
/// asientos y su precio) guarda acá el contexto de la compra — tipo, cantidad, auto-renovación y precio —
/// mientras PaymentApp solo la correlaciona por <c>SaaSPayment.TargetAggregateId</c> y permanece agnóstico a
/// los asientos. Al confirmarse el pago, un consumer aprovisiona los asientos (crea + activa) sin volver a
/// cobrar. El camino con método en archivo NO usa esta intención: va por el cobro off-session directo.
/// </summary>
public sealed class SeatPurchaseIntent : TenantEntity
{
    public SeatType SeatType { get; private set; }
    public int Quantity { get; private set; }
    public bool AutoRenew { get; private set; }
    public Money UnitPrice { get; private set; } = null!;
    public BillingCycle BillingCycle { get; private set; }
    public long ProratedTotalCents { get; private set; }
    public string Currency { get; private set; } = default!;
    public SeatPurchaseIntentStatus Status { get; private set; }
    public Guid? SaaSPaymentId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    private SeatPurchaseIntent() { }

    public static Result<SeatPurchaseIntent> Create(
        Guid tenantId,
        SeatType seatType,
        int quantity,
        bool autoRenew,
        Money unitPrice,
        BillingCycle billingCycle,
        long proratedTotalCents,
        Guid requestedByUserId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SeatPurchaseIntent>(
                new Error("SeatPurchaseIntent.InvalidTenant", "TenantId is required.")
            );

        if (quantity is < 1 or > 500)
            return Result.Failure<SeatPurchaseIntent>(
                new Error("SeatPurchaseIntent.InvalidQuantity", "Quantity must be between 1 and 500.")
            );

        // El hosted-checkout es para cobrar: un total 0 (tipo gratis) no debe llegar acá — va por compra directa.
        if (proratedTotalCents <= 0)
            return Result.Failure<SeatPurchaseIntent>(
                new Error("SeatPurchaseIntent.NothingToCharge", "Checkout requires a positive prorated total.")
            );

        var intent = new SeatPurchaseIntent
        {
            SeatType = seatType,
            Quantity = quantity,
            AutoRenew = autoRenew,
            UnitPrice = unitPrice,
            BillingCycle = billingCycle,
            ProratedTotalCents = proratedTotalCents,
            Currency = unitPrice.Currency,
            Status = SeatPurchaseIntentStatus.Pending,
            RequestedByUserId = requestedByUserId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        intent.SetTenant(tenantId);
        return Result.Success(intent);
    }

    /// <summary>Guarda la referencia al pago y la URL de checkout una vez creada la sesión en PaymentApp.</summary>
    public Result AttachCheckout(Guid saaSPaymentId, string checkoutUrl, DateTime nowUtc)
    {
        if (Status != SeatPurchaseIntentStatus.Pending)
            return Result.Failure(
                new Error("SeatPurchaseIntent.InvalidTransition", $"Cannot attach a checkout while {Status}.")
            );

        SaaSPaymentId = saaSPaymentId;
        CheckoutUrl = checkoutUrl;
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>Marca el pago confirmado (webhook/reconcile). Idempotente.</summary>
    public Result MarkPaid(DateTime paidAtUtc)
    {
        if (Status is SeatPurchaseIntentStatus.Paid or SeatPurchaseIntentStatus.Provisioned)
            return Result.Success();

        if (Status != SeatPurchaseIntentStatus.Pending)
            return Result.Failure(
                new Error("SeatPurchaseIntent.InvalidTransition", $"Cannot mark paid while {Status}.")
            );

        Status = SeatPurchaseIntentStatus.Paid;
        PaidAtUtc = paidAtUtc;
        Touch(paidAtUtc);
        return Result.Success();
    }

    /// <summary>Marca los asientos ya aprovisionados. Idempotente; requiere haber sido pagada.</summary>
    public Result MarkProvisioned(DateTime nowUtc)
    {
        if (Status == SeatPurchaseIntentStatus.Provisioned)
            return Result.Success();

        if (Status != SeatPurchaseIntentStatus.Paid)
            return Result.Failure(
                new Error("SeatPurchaseIntent.NotPaid", "Only a paid intent can be marked provisioned.")
            );

        Status = SeatPurchaseIntentStatus.Provisioned;
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>Marca el fallo/expiración del pago. Idempotente; nunca revierte una intención ya aprovisionada.</summary>
    public Result MarkFailed(string reason, DateTime nowUtc)
    {
        if (Status == SeatPurchaseIntentStatus.Failed)
            return Result.Success();

        if (Status == SeatPurchaseIntentStatus.Provisioned)
            return Result.Failure(
                new Error("SeatPurchaseIntent.AlreadyProvisioned", "Cannot fail an already provisioned intent.")
            );

        Status = SeatPurchaseIntentStatus.Failed;
        FailureReason = reason;
        Touch(nowUtc);
        return Result.Success();
    }

    private void Touch(DateTime nowUtc) => UpdatedAtUtc = nowUtc;
}
