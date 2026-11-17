using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;

/// <summary>
/// PayFlow onboarding checkout. The payer email comes from the OTP-verified onboarding email;
/// provider/method are selected from PaymentApp's catalog; price/currency are still resolved
/// server-side through Subscription.
/// </summary>
public sealed record CreateOnboardingCheckoutCommand(
    Guid OnboardingId,
    Guid PlanId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    PaymentProviderCode Provider = PaymentProviderCode.Stripe,
    PaymentMethodKind Method = PaymentMethodKind.Card,
    string BillingCycle = "Monthly",
    long? NetAmountCents = null,
    long? DiscountAmountCents = null,
    string? Currency = null,
    Guid? CodeReservationId = null,
    string? PromotionSnapshotHash = null,
    /// <summary>Nombre comercial del plan comprado (ej. "Professional"), para el recibo.</summary>
    string? PlanDisplayName = null,
    /// <summary>El comprador declaró un código de referido; se guarda en el pago y viaja en su evento.</summary>
    bool? ReferralDeclared = null,
    /// <summary>Todos los códigos del alta (promo y regalo), uno por reserva de Growth.</summary>
    IReadOnlyList<OnboardingCheckoutPromotion>? Promotions = null
);

/// <summary>Un código de descuento del alta: su reserva en Growth, el descuento y su orden.</summary>
public sealed record OnboardingCheckoutPromotion(
    Guid CodeReservationId,
    long DiscountAmountCents,
    string? PromotionSnapshotHash,
    int Order
);

public sealed record OnboardingCheckoutResponse(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);
