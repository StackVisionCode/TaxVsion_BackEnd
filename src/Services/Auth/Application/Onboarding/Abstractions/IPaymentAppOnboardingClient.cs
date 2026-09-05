using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>
/// Auth-to-PaymentApp client for pay-first onboarding. Auth transports the buyer's selected
/// provider/method, while PaymentApp remains the owner of catalog, validation, checkout, webhook
/// and reconciliation.
/// </summary>
public interface IPaymentAppOnboardingClient
{
    Task<Result<PaymentAppCheckoutResult>> CreateCheckoutAsync(
        PaymentAppCheckoutRequest request,
        CancellationToken ct = default
    );

    Task<Result<PaymentAppPaymentOptionsResult>> GetPaymentOptionsAsync(
        PaymentAppPaymentOptionsRequest request,
        CancellationToken ct = default
    );

    Task<Result<PaymentAppReconcileResult>> ReconcileCheckoutAsync(
        PaymentAppReconcileRequest request,
        CancellationToken ct = default
    );
}

public sealed record PaymentAppCheckoutRequest(
    Guid OnboardingId,
    Guid PlanId,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    string Provider = "Stripe",
    string Method = "Card",
    string BillingCycle = "Monthly",
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

public sealed record PaymentAppPaymentOptionsRequest(Guid PlanId, string BillingCycle, string? Currency = null);

public sealed record PaymentAppPaymentOptionsResult(IReadOnlyList<PaymentAppPaymentOption> Options);

public sealed record PaymentAppPaymentOption(
    string Provider,
    string Method,
    string DisplayName,
    bool Enabled,
    int Priority,
    string? DisabledReason
);

public sealed record PaymentAppReconcileRequest(Guid PaymentId);

public sealed record PaymentAppReconcileResult(
    Guid PaymentId,
    OnboardingPaymentStatus Status,
    long AmountPaidCents,
    string Currency,
    string? FailureCode,
    string? FailureMessage,
    string? ProviderPaymentReference,
    DateTime? PaidAtUtc
);
