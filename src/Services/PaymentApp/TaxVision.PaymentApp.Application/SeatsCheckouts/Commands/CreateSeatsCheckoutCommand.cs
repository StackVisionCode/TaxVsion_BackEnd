using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SeatsCheckouts.Commands;

/// <summary>
/// Crea (o replay idempotente) una sesión de checkout hosteada para comprar asientos extra, cuando el tenant
/// NO tiene método de pago en archivo. Molde: <c>CreateOnboardingCheckoutCommand</c>, pero más simple — el
/// tenant YA existe y Subscription (dueño del precio de asiento) ya resolvió el total prorrateado, así que
/// acá NO se resuelve precio ni promociones: llega <see cref="AmountCents"/>/<see cref="Currency"/> listos.
/// <see cref="SeatPurchaseIntentId"/> viaja como <c>TargetAggregateId</c> del pago y correlaciona el webhook
/// con la intención de Subscription que aprovisiona los asientos.
/// </summary>
public sealed record CreateSeatsCheckoutCommand(
    Guid TenantId,
    Guid SeatPurchaseIntentId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    PaymentProviderCode Provider = PaymentProviderCode.Stripe,
    PaymentMethodKind Method = PaymentMethodKind.Card
);

public sealed record SeatsCheckoutResponse(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);
