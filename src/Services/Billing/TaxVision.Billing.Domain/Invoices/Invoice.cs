using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>
/// Aggregate root del documento factura tenant→taxpayer. Dueño único de su ciclo de vida,
/// totales congelados, líneas y enlaces de pago. No conoce providers de pago, render de PDF,
/// envío de email ni el bus — solo junta domain events; drenarlos es del DbContext.
///
/// SCAFFOLD B1: la máquina de estados y las transiciones completas (Issue/MarkSent/RecordPayment/
/// Void/…) se implementan en la fase B2 (ver documents/architecture/billing/15_Billing_Implementation_Plan.md).
/// </summary>
public sealed class Invoice : AggregateRoot
{
    private readonly List<InvoiceLineItem> _lines = [];
    private readonly List<InvoicePaymentLink> _paymentLinks = [];

    public string? InvoiceNumber { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTime IssueDateUtc { get; private set; }
    public DateTime DueDateUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }

    public CustomerSnapshot Customer { get; private set; } = null!;
    public IssuerSnapshot? Issuer { get; private set; }
    public Discount? Discount { get; private set; }

    public Money Subtotal { get; private set; } = null!;
    public Money TaxTotal { get; private set; } = null!;
    public Money DiscountTotal { get; private set; } = null!;
    public Money Total { get; private set; } = null!;
    public Money AmountPaid { get; private set; } = null!;
    public Money AmountDue { get; private set; } = null!;
    public string Currency { get; private set; } = "USD";

    public string? PoNumber { get; private set; }
    public string? Summary { get; private set; }
    public string? Notes { get; private set; }
    public PaymentMethod? PaymentMethod { get; private set; }

    public Guid? PdfFileId { get; private set; }
    public Guid? PaidPdfFileId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? LastModifiedBy { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<InvoiceLineItem> Lines => _lines;
    public IReadOnlyCollection<InvoicePaymentLink> PaymentLinks => _paymentLinks;

    private Invoice() { }

    /// <summary>SCAFFOLD B1: placeholder. La fábrica real (validación de líneas, moneda, fechas,
    /// cálculo de totales provisionales, snapshots) se implementa en B2.</summary>
    public static Result<Invoice> CreateDraft(Guid tenantId, Guid actorUserId)
    {
        _ = tenantId;
        _ = actorUserId;
        return Result.Failure<Invoice>(
            new Error("Billing.NotImplemented", "Invoice.CreateDraft is scaffolded; implemented in phase B2.")
        );
    }
}
