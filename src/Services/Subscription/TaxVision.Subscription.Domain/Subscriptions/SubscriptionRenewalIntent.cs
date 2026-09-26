using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Subscriptions;

public enum SubscriptionRenewalIntentStatus
{
    /// <summary>Creada; se emitió (o se está emitiendo) la sesión de checkout, esperando el pago del dueño.</summary>
    Pending,

    /// <summary>Pago confirmado por el proveedor (webhook/reconcile); pendiente de reactivar la suscripción.</summary>
    Paid,

    /// <summary>Suscripción reactivada a partir de esta intención.</summary>
    Provisioned,

    /// <summary>El pago falló o la sesión expiró sin pagar.</summary>
    Failed,
}

/// <summary>
/// Intención de renovación/reactivación de la suscripción base por HOSTED-CHECKOUT (redirect), para el tenant
/// cuya suscripción cayó en lapso (PastDue/GracePeriod/Suspended/Expired) y no tiene método de pago en archivo.
/// Espejo de <see cref="Seats.SeatPurchaseIntent"/>: Subscription guarda acá el contexto del pago (monto y
/// ciclo) mientras PaymentApp solo lo correlaciona por <c>SaaSPayment.TargetAggregateId</c>. Al confirmarse el
/// pago, un consumer reactiva la suscripción (nuevo período) sin volver a cobrar.
/// </summary>
public sealed class SubscriptionRenewalIntent : TenantEntity
{
    public long AmountCents { get; private set; }
    public string Currency { get; private set; } = default!;
    public BillingCycle BillingCycle { get; private set; }
    public SubscriptionRenewalIntentStatus Status { get; private set; }
    public Guid? SaaSPaymentId { get; private set; }
    public string? CheckoutUrl { get; private set; }

    /// <summary>Cuándo caduca la sesión del proveedor. Mientras no caduque, esta intención sigue siendo
    /// pagable: abrir otra en paralelo arriesga un doble cobro.</summary>
    public DateTime? CheckoutExpiresAtUtc { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    private SubscriptionRenewalIntent() { }

    public static Result<SubscriptionRenewalIntent> Create(
        Guid tenantId,
        long amountCents,
        string currency,
        BillingCycle billingCycle,
        Guid requestedByUserId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SubscriptionRenewalIntent>(
                new Error("SubscriptionRenewalIntent.InvalidTenant", "TenantId is required.")
            );

        // El hosted-checkout es para cobrar: un total 0 no debe llegar acá.
        if (amountCents <= 0)
            return Result.Failure<SubscriptionRenewalIntent>(
                new Error("SubscriptionRenewalIntent.NothingToCharge", "Checkout requires a positive amount.")
            );

        if (string.IsNullOrWhiteSpace(currency))
            return Result.Failure<SubscriptionRenewalIntent>(
                new Error("SubscriptionRenewalIntent.InvalidCurrency", "Currency is required.")
            );

        var intent = new SubscriptionRenewalIntent
        {
            AmountCents = amountCents,
            Currency = currency,
            BillingCycle = billingCycle,
            Status = SubscriptionRenewalIntentStatus.Pending,
            RequestedByUserId = requestedByUserId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        intent.SetTenant(tenantId);
        return Result.Success(intent);
    }

    /// <summary>Guarda la referencia al pago y la URL de checkout una vez creada la sesión en PaymentApp.</summary>
    public Result AttachCheckout(Guid saaSPaymentId, string checkoutUrl, DateTime expiresAtUtc, DateTime nowUtc)
    {
        if (Status != SubscriptionRenewalIntentStatus.Pending)
            return Result.Failure(
                new Error("SubscriptionRenewalIntent.InvalidTransition", $"Cannot attach a checkout while {Status}.")
            );

        SaaSPaymentId = saaSPaymentId;
        CheckoutUrl = checkoutUrl;
        CheckoutExpiresAtUtc = expiresAtUtc;
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>¿Sigue viva? Reutilizarla es lo que evita el doble cobro.</summary>
    public bool IsOpen(DateTime nowUtc) =>
        Status == SubscriptionRenewalIntentStatus.Pending && CheckoutUrl is not null && CheckoutExpiresAtUtc > nowUtc;

    /// <summary>Marca el pago confirmado (webhook/reconcile). Idempotente.</summary>
    public Result MarkPaid(DateTime paidAtUtc)
    {
        if (Status is SubscriptionRenewalIntentStatus.Paid or SubscriptionRenewalIntentStatus.Provisioned)
            return Result.Success();

        if (Status != SubscriptionRenewalIntentStatus.Pending)
            return Result.Failure(
                new Error("SubscriptionRenewalIntent.InvalidTransition", $"Cannot mark paid while {Status}.")
            );

        Status = SubscriptionRenewalIntentStatus.Paid;
        PaidAtUtc = paidAtUtc;
        Touch(paidAtUtc);
        return Result.Success();
    }

    /// <summary>Marca la suscripción ya reactivada. Idempotente; requiere haber sido pagada.</summary>
    public Result MarkProvisioned(DateTime nowUtc)
    {
        if (Status == SubscriptionRenewalIntentStatus.Provisioned)
            return Result.Success();

        if (Status != SubscriptionRenewalIntentStatus.Paid)
            return Result.Failure(
                new Error("SubscriptionRenewalIntent.NotPaid", "Only a paid intent can be marked provisioned.")
            );

        Status = SubscriptionRenewalIntentStatus.Provisioned;
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>Marca el fallo/expiración del pago. Idempotente; nunca revierte una intención ya aprovisionada.</summary>
    public Result MarkFailed(string reason, DateTime nowUtc)
    {
        if (Status == SubscriptionRenewalIntentStatus.Failed)
            return Result.Success();

        if (Status == SubscriptionRenewalIntentStatus.Provisioned)
            return Result.Failure(
                new Error("SubscriptionRenewalIntent.AlreadyProvisioned", "Cannot fail an already provisioned intent.")
            );

        Status = SubscriptionRenewalIntentStatus.Failed;
        FailureReason = reason;
        Touch(nowUtc);
        return Result.Success();
    }

    private void Touch(DateTime nowUtc) => UpdatedAtUtc = nowUtc;
}
