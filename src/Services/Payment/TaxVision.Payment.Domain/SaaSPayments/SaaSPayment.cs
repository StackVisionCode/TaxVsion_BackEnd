using BuildingBlocks.Domain;

namespace TaxVision.Payment.Domain.SaaSPayments;

/// <summary>
/// Aggregate root representing a SaaS platform payment: a charge that TaxVision initiates
/// against a tenant's Stripe customer on behalf of the platform (e.g. enrollment fee,
/// seat purchase, subscription renewal).
/// <para>
/// Lifecycle: <c>Pending</c> → <c>Processing</c> (after PaymentIntent created) →
/// <c>Completed</c> | <c>Failed</c> (after Stripe webhook confirmation).
/// </para>
/// </summary>
public sealed class SaaSPayment : TenantEntity
{
    /// <summary>Categorizes this payment (Enrollment, SeatPurchase, SeatRenewal, SubscriptionRenewal).</summary>
    public SaaSPaymentType PaymentType { get; private set; }

    /// <summary>Current lifecycle status of the payment.</summary>
    public PaymentStatus Status { get; private set; }

    /// <summary>Amount charged in the smallest currency unit (e.g. cents for USD).</summary>
    public long AmountCents { get; private set; }

    /// <summary>ISO 4217 currency code (default "USD").</summary>
    public string Currency { get; private set; } = "USD";

    /// <summary>
    /// Stripe PaymentIntent ID assigned after <see cref="MarkProcessing"/> is called.
    /// Used by the webhook handler to correlate Stripe events back to this record.
    /// </summary>
    public string? StripePaymentIntentId { get; private set; }

    /// <summary>
    /// Business reference ID (enrollment ID, seat subscription ID, or subscription ID)
    /// that triggered this payment.
    /// </summary>
    public Guid ReferenceId { get; private set; }

    /// <summary>UTC timestamp when this record was created.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>UTC timestamp of the last status transition.</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Human-readable reason provided by Stripe when the payment fails, or <c>null</c>.</summary>
    public string? FailureReason { get; private set; }

    private SaaSPayment() { }

    /// <summary>
    /// Creates a new <see cref="SaaSPayment"/> in <c>Pending</c> status.
    /// Call <see cref="MarkProcessing"/> after creating the Stripe PaymentIntent.
    /// </summary>
    public static SaaSPayment Create(
        Guid tenantId,
        SaaSPaymentType paymentType,
        long amountCents,
        string currency,
        Guid referenceId)
    {
        var payment = new SaaSPayment
        {
            PaymentType = paymentType,
            Status = PaymentStatus.Pending,
            AmountCents = amountCents,
            Currency = currency,
            ReferenceId = referenceId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        payment.SetTenant(tenantId);
        return payment;
    }

    /// <summary>
    /// Transitions to <c>Processing</c> and records the Stripe PaymentIntent ID.
    /// Must be called before the Stripe confirmation step.
    /// </summary>
    public void MarkProcessing(string stripePaymentIntentId)
    {
        StripePaymentIntentId = stripePaymentIntentId;
        Status = PaymentStatus.Processing;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Transitions to <c>Completed</c>. Called after a successful Stripe webhook event.</summary>
    public void MarkCompleted()
    {
        Status = PaymentStatus.Completed;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Transitions to <c>Failed</c> and records the reason. Called after a failed Stripe webhook event.</summary>
    public void MarkFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
