using BuildingBlocks.Domain;

namespace TaxVision.Payment.Domain.SaaSPayments;

public sealed class SaaSPayment : TenantEntity
{
    public SaaSPaymentType PaymentType { get; private set; }
    public PaymentStatus Status { get; private set; }
    public long AmountCents { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string? StripePaymentIntentId { get; private set; }
    public Guid ReferenceId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    private SaaSPayment() { }

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

    public void MarkProcessing(string stripePaymentIntentId)
    {
        StripePaymentIntentId = stripePaymentIntentId;
        Status = PaymentStatus.Processing;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkCompleted()
    {
        Status = PaymentStatus.Completed;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
