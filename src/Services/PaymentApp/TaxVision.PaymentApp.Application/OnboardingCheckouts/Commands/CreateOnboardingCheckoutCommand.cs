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
    string? PromotionSnapshotHash = null
);

public sealed record OnboardingCheckoutResponse(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);
