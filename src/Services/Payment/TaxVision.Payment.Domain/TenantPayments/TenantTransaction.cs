using BuildingBlocks.Domain;
using TaxVision.Payment.Domain.SaaSPayments;

namespace TaxVision.Payment.Domain.TenantPayments;

/// <summary>
/// Records a payment transaction initiated by a tenant against one of its own customers
/// (tenant-side payment context). Each row corresponds to a single charge attempt through
/// the tenant's configured payment provider.
/// <para>
/// This entity is independent of the SaaS billing context (<see cref="SaaSPayment"/>).
/// Tenants manage their own provider credentials via <see cref="TenantPaymentConfig"/>.
/// </para>
/// </summary>
public sealed class TenantTransaction : TenantEntity
{
    /// <summary>Optional reference to the tenant's customer record, if available.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>Payment provider used for this transaction.</summary>
    public TenantPaymentProvider Provider { get; private set; }

    /// <summary>Current status of the transaction.</summary>
    public PaymentStatus Status { get; private set; }

    /// <summary>Amount in the smallest currency unit (e.g. cents for USD).</summary>
    public long AmountCents { get; private set; }

    /// <summary>ISO 4217 currency code (default "USD").</summary>
    public string Currency { get; private set; } = "USD";

    /// <summary>Transaction ID assigned by the external payment provider after successful completion.</summary>
    public string? ExternalTransactionId { get; private set; }

    /// <summary>Human-readable description of the charge (e.g. invoice number).</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>UTC timestamp when this transaction record was created.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>UTC timestamp of the last status transition.</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Failure message from the provider, or <c>null</c> on success.</summary>
    public string? FailureReason { get; private set; }

    private TenantTransaction() { }

    /// <summary>Creates a new <see cref="TenantTransaction"/> in <c>Pending</c> status.</summary>
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

    /// <summary>
    /// Transitions to <c>Completed</c> and records the external transaction ID returned by the provider.
    /// </summary>
    public void MarkCompleted(string externalTransactionId)
    {
        Status = PaymentStatus.Completed;
        ExternalTransactionId = externalTransactionId;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Transitions to <c>Failed</c> and records the provider's error message.</summary>
    public void MarkFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
