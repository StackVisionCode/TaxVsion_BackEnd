using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.AddOnCheckouts.Commands;

/// <summary>
/// Crea (o replay idempotente) la sesión de checkout hosteada para comprar un add-on, cuando el tenant NO
/// tiene método de pago en archivo. Molde: <c>CreateSeatsCheckoutCommand</c> — el tenant ya existe y
/// Subscription (dueño del catálogo y del prorrateo) ya resolvió el total, así que acá NO se resuelve precio.
/// <see cref="AddOnPurchaseIntentId"/> viaja como <c>TargetAggregateId</c> del pago y correlaciona el webhook
/// con la intención que activa el add-on.
/// </summary>
public sealed record CreateAddOnCheckoutCommand(
    Guid TenantId,
    Guid AddOnPurchaseIntentId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    /// <summary>Unidades y precio unitario del cobro, para el recibo. Opcionales: solo los llevan los
    /// cobros que tienen algo que contar, y PaymentApp los descarta si no cuadran con el importe.</summary>
    int? Quantity = null,
    long? UnitAmountCents = null,
    PaymentProviderCode Provider = PaymentProviderCode.Stripe,
    PaymentMethodKind Method = PaymentMethodKind.Card
);

public sealed record AddOnCheckoutResponse(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);
