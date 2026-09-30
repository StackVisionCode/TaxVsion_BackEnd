using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.AddOns;

public enum AddOnPurchaseIntentStatus
{
    /// <summary>Creada; se emitió (o se está emitiendo) la sesión de checkout, esperando el pago del cliente.</summary>
    Pending,

    /// <summary>Pago confirmado por el proveedor (webhook/reconcile); pendiente de activar el add-on.</summary>
    Paid,

    /// <summary>El add-on quedó activo a partir de esta intención.</summary>
    Provisioned,

    /// <summary>El pago falló o la sesión expiró sin pagar.</summary>
    Failed,
}

/// <summary>
/// Intención de compra de un add-on por HOSTED-CHECKOUT (redirect), para el tenant que NO tiene método de
/// pago en archivo. Molde: <see cref="Seats.SeatPurchaseIntent"/>. Subscription guarda acá el contexto de la
/// compra — qué add-on, cuántos, auto-renovación y el prorrateo ya calculado — y PaymentApp solo la
/// correlaciona por <c>SaaSPayment.TargetAggregateId</c>. El <c>TenantAddOn</c> NO se crea hasta que el pago
/// se confirma: así nadie queda con un add-on activo sin haber pagado. La compra off-session
/// (<c>POST /addons</c>) no usa esta intención.
/// </summary>
public sealed class AddOnPurchaseIntent : TenantEntity
{
    public Guid AddOnDefinitionId { get; private set; }
    public string AddOnCode { get; private set; } = default!;
    public int Quantity { get; private set; }
    public bool AutoRenew { get; private set; }
    public Money UnitPrice { get; private set; } = null!;
    public BillingCycle BillingCycle { get; private set; }
    public long ProratedTotalCents { get; private set; }
    public string Currency { get; private set; } = default!;
    public AddOnPurchaseIntentStatus Status { get; private set; }
    public Guid? SaaSPaymentId { get; private set; }
    public string? CheckoutUrl { get; private set; }

    /// <summary>Cuándo caduca la sesión de checkout del proveedor. Mientras no caduque, esta intención sigue
    /// siendo pagable: crear otra en paralelo arriesga un doble cobro.</summary>
    public DateTime? CheckoutExpiresAtUtc { get; private set; }

    /// <summary>El add-on que se activó al pagar; queda para no volver a crearlo en una redelivery.</summary>
    public Guid? TenantAddOnId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    private AddOnPurchaseIntent() { }

    public static Result<AddOnPurchaseIntent> Create(
        Guid tenantId,
        AddOnDefinition definition,
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
            return Result.Failure<AddOnPurchaseIntent>(
                new Error("AddOnPurchaseIntent.InvalidTenant", "TenantId is required.")
            );

        if (quantity < 1)
            return Result.Failure<AddOnPurchaseIntent>(
                new Error("AddOnPurchaseIntent.InvalidQuantity", "Quantity must be at least 1.")
            );

        // El hosted-checkout es para cobrar: un total 0 no debe llegar acá — va por compra directa.
        if (proratedTotalCents <= 0)
            return Result.Failure<AddOnPurchaseIntent>(
                new Error("AddOnPurchaseIntent.NothingToCharge", "Checkout requires a positive prorated total.")
            );

        var intent = new AddOnPurchaseIntent
        {
            AddOnDefinitionId = definition.Id,
            AddOnCode = definition.Code.Value,
            Quantity = quantity,
            AutoRenew = autoRenew,
            UnitPrice = unitPrice,
            BillingCycle = billingCycle,
            ProratedTotalCents = proratedTotalCents,
            Currency = unitPrice.Currency,
            Status = AddOnPurchaseIntentStatus.Pending,
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
        if (Status != AddOnPurchaseIntentStatus.Pending)
            return Result.Failure(
                new Error("AddOnPurchaseIntent.InvalidTransition", $"Cannot attach a checkout while {Status}.")
            );

        SaaSPaymentId = saaSPaymentId;
        CheckoutUrl = checkoutUrl;
        CheckoutExpiresAtUtc = expiresAtUtc;
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>¿Sigue viva? Con la sesión aún pagable, reutilizarla es lo que evita el doble cobro.</summary>
    public bool IsOpen(DateTime nowUtc) =>
        Status == AddOnPurchaseIntentStatus.Pending && CheckoutUrl is not null && CheckoutExpiresAtUtc > nowUtc;

    /// <summary>¿Es exactamente la misma compra que se está pidiendo otra vez?</summary>
    public bool Matches(string addOnCode, int quantity, BillingCycle billingCycle, bool autoRenew) =>
        string.Equals(AddOnCode, addOnCode, StringComparison.OrdinalIgnoreCase)
        && Quantity == quantity
        && BillingCycle == billingCycle
        && AutoRenew == autoRenew;

    /// <summary>Marca el pago confirmado (webhook/reconcile). Idempotente.</summary>
    public Result MarkPaid(DateTime paidAtUtc)
    {
        if (Status is AddOnPurchaseIntentStatus.Paid or AddOnPurchaseIntentStatus.Provisioned)
            return Result.Success();

        if (Status != AddOnPurchaseIntentStatus.Pending)
            return Result.Failure(
                new Error("AddOnPurchaseIntent.InvalidTransition", $"Cannot mark paid while {Status}.")
            );

        Status = AddOnPurchaseIntentStatus.Paid;
        PaidAtUtc = paidAtUtc;
        Touch(paidAtUtc);
        return Result.Success();
    }

    /// <summary>Marca el add-on ya activado. Idempotente; requiere haber sido pagada.</summary>
    public Result MarkProvisioned(Guid tenantAddOnId, DateTime nowUtc)
    {
        if (Status == AddOnPurchaseIntentStatus.Provisioned)
            return Result.Success();

        if (Status != AddOnPurchaseIntentStatus.Paid)
            return Result.Failure(
                new Error("AddOnPurchaseIntent.NotPaid", "Only a paid intent can be marked provisioned.")
            );

        Status = AddOnPurchaseIntentStatus.Provisioned;
        TenantAddOnId = tenantAddOnId;
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>Marca el fallo/expiración del pago. Idempotente; nunca revierte una intención ya aprovisionada.</summary>
    public Result MarkFailed(string reason, DateTime nowUtc)
    {
        if (Status == AddOnPurchaseIntentStatus.Failed)
            return Result.Success();

        if (Status == AddOnPurchaseIntentStatus.Provisioned)
            return Result.Failure(
                new Error("AddOnPurchaseIntent.AlreadyProvisioned", "Cannot fail an already provisioned intent.")
            );

        Status = AddOnPurchaseIntentStatus.Failed;
        FailureReason = reason;
        Touch(nowUtc);
        return Result.Success();
    }

    private void Touch(DateTime nowUtc) => UpdatedAtUtc = nowUtc;
}
