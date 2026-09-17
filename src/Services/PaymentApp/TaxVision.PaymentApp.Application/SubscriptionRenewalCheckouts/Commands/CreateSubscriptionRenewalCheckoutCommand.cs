using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts.Commands;

/// <summary>Crea la sesión de checkout hosteada para una renovación/reactivación self-service de la
/// suscripción base (tenant sin método en archivo cuya suscripción cayó en lapso). Molde:
/// <c>CreateSeatsCheckoutCommand</c>; <c>RenewalIntentId</c> viaja como <c>SaaSPayment.TargetAggregateId</c>.</summary>
public sealed record CreateSubscriptionRenewalCheckoutCommand(
    Guid TenantId,
    Guid RenewalIntentId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    PaymentProviderCode Provider = PaymentProviderCode.Stripe,
    PaymentMethodKind Method = PaymentMethodKind.Card
);

public sealed record SubscriptionRenewalCheckoutResponse(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);
