using BuildingBlocks.Domain;
using TaxVision.Payment.Domain.SaaSPayments;

namespace TaxVision.Payment.Domain.TenantPayments;

public sealed class TenantTransaction : TenantEntity
{
    public Guid? CustomerId { get; private set; }
    public TenantPaymentProvider Provider { get; private set; }
    public PaymentStatus Status { get; private set; }
    public long AmountCents { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string? ExternalTransactionId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    private TenantTransaction() { }

    public static TenantTransaction Create(
        Guid tenantId,
        Guid? customerId,
        TenantPaymentProvider provider,
        long amountCents,
        string currency,
        string description)
    {
        var transaction = new TenantTransaction
        {
            CustomerId = customerId,
            Provider = provider,
            Status = PaymentStatus.Pending,
            AmountCents = amountCents,
            Currency = currency,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        transaction.SetTenant(tenantId);
        return transaction;
    }

    public void MarkCompleted(string externalTransactionId)
    {
        Status = PaymentStatus.Completed;
        ExternalTransactionId = externalTransactionId;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
