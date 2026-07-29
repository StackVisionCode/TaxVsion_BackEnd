namespace TaxVision.PaymentApp.Application.OnboardingCheckouts.Commands;

/// <summary>PayFlow (Fase 8/16) — <paramref name="PayerEmail"/> viene del email ya verificado
/// (OTP, Auth Fase 5) del onboarding: se pasa a Stripe para prellenar/bloquear el campo en la
/// página de checkout hosteada, en vez de pedírselo de nuevo al pagador. Deliberadamente NO recibe
/// precio/moneda — <see cref="CreateOnboardingCheckoutHandler"/> los resuelve server-side vía M2M a
/// Subscription (Fase 16), cerrando el price-trust gap que existía hasta esa fase.</summary>
public sealed record CreateOnboardingCheckoutCommand(
    Guid OnboardingId,
    Guid PlanId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey
);

public sealed record OnboardingCheckoutResponse(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);
