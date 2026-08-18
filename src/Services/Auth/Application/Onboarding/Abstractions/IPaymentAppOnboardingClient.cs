using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>PayFlow (Fase 9) — Auth calling PaymentApp's M2M checkout endpoint (Fase 8). The
/// implementation mints its own service token in-process (Auth hosts the token generator
/// itself) instead of round-tripping through <c>POST auth/service-token</c> like every other
/// service does — see <c>PaymentAppOnboardingClient</c> for why.</summary>
public interface IPaymentAppOnboardingClient
{
    Task<Result<PaymentAppCheckoutResult>> CreateCheckoutAsync(
        PaymentAppCheckoutRequest request,
        CancellationToken ct = default
    );
}

/// <summary>PayFlow (Fase 16) — deliberadamente sin precio/moneda: PaymentApp los resuelve
/// server-side vía M2M a Subscription antes de crear el Stripe Checkout Session.</summary>
public sealed record PaymentAppCheckoutRequest(
    Guid OnboardingId,
    Guid PlanId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    // Ciclo elegido ("Monthly"/"Yearly") — PaymentApp resuelve el bruto de ESE ciclo en Subscription.
    string BillingCycle = "Monthly",
    // Gift/Referral: si un código aplicó descuento (parcial), el NETO a cobrar (override del bruto) +
    // el resumen de la reserva para trazabilidad. Null = sin código → PaymentApp resuelve el bruto.
    long? NetAmountCents = null,
    long? DiscountAmountCents = null,
    string? Currency = null,
    Guid? CodeReservationId = null,
    string? PromotionSnapshotHash = null
);

public sealed record PaymentAppCheckoutResult(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);
