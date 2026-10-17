using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SeatsCheckouts.Commands;

/// <summary>Checkout hosteado para reactivar un seat Suspended/PastDue por pago manual del admin. A diferencia
/// de <see cref="CreateSeatsCheckoutCommand"/> (compra nueva prorrateada), este cobra 1 período completo del
/// seat existente y el pago lleva <c>SaaSPaymentType.SeatRenewal</c> + <c>TargetAggregateId = seatId</c>, de
/// modo que al confirmarse dispara el publisher existente de <c>SeatRenewalPaymentSucceeded</c> y el consumer
/// de Subscription llama <c>SubscriptionSeat.CompleteReactivation</c> por detectar estado Suspended/PastDue.</summary>
public sealed record CreateSeatReactivationCheckoutCommand(
    Guid TenantId,
    Guid SeatId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    PaymentProviderCode Provider = PaymentProviderCode.Stripe,
    PaymentMethodKind Method = PaymentMethodKind.Card,
    string? AmountSignature = null
);
