using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Receipts;

/// <summary>
/// Aggregate root del comprobante de pago verificable. Sella un pago aplicado a una factura con
/// un VerificationHash SHA-256 auto-verificable (verificación pública anónima).
///
/// SCAFFOLD B1: Issue/Void/MarkRefunded/ValidateHash se implementan en la fase B3.
/// </summary>
public sealed class PaymentReceipt : AggregateRoot
{
    public string ReceiptNumber { get; private set; } = string.Empty;
    public Guid InvoiceId { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public CustomerSnapshot Customer { get; private set; } = null!;
    public Money AmountPaid { get; private set; } = null!;
    public PaymentMethod PaymentMethod { get; private set; }
    public string PaymentReference { get; private set; } = string.Empty;
    public DateTime PaymentDateUtc { get; private set; }
    public DateTime IssuedDateUtc { get; private set; }
    public string VerificationHash { get; private set; } = string.Empty;
    public ReceiptStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public Guid? PdfFileId { get; private set; }
    public Guid? ProcessedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private PaymentReceipt() { }

    /// <summary>SCAFFOLD B1: placeholder. La emisión real (cómputo del hash, evento ReceiptIssued)
    /// se implementa en B3.</summary>
    public static Result<PaymentReceipt> Issue(Guid tenantId, Guid invoiceId)
    {
        _ = tenantId;
        _ = invoiceId;
        return Result.Failure<PaymentReceipt>(
            new Error("Billing.NotImplemented", "PaymentReceipt.Issue is scaffolded; implemented in phase B3.")
        );
    }
}
