using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>Línea de detalle de una factura. Entidad interna del aggregate <see cref="Invoice"/>.
/// Los montos se congelan al emitir; el cálculo de impuesto/precio es responsabilidad del caller
/// (o de un futuro Catalog), Billing solo lo snapshotea.</summary>
public sealed class InvoiceLineItem : BaseEntity
{
    public Guid InvoiceId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public Money UnitAmount { get; private set; } = null!;
    public int TaxBasisPoints { get; private set; }
    public Money TaxAmount { get; private set; } = null!;
    public Money LineTotal { get; private set; } = null!;

    private InvoiceLineItem() { }

    // NOTE (scaffold B1): fábrica y validación completas se implementan en B2.
    internal InvoiceLineItem(
        Guid invoiceId,
        string description,
        int quantity,
        Money unitAmount,
        int taxBasisPoints,
        Money taxAmount,
        Money lineTotal
    )
    {
        InvoiceId = invoiceId;
        Description = description;
        Quantity = quantity;
        UnitAmount = unitAmount;
        TaxBasisPoints = taxBasisPoints;
        TaxAmount = taxAmount;
        LineTotal = lineTotal;
    }
}

/// <summary>Registro interno del cobro asociado a una factura, para correlacionar los eventos
/// <c>payments.*</c> de PaymentClient (ver BDR-001 en la documentación de diseño).</summary>
public sealed class InvoicePaymentLink : BaseEntity
{
    public Guid InvoiceId { get; private set; }
    public string PaymentSource { get; private set; } = string.Empty;
    public Guid PaymentId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? PayUrl { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private InvoicePaymentLink() { }

    internal InvoicePaymentLink(Guid invoiceId, string paymentSource, Guid paymentId, string status, string? payUrl, DateTime createdAtUtc)
    {
        InvoiceId = invoiceId;
        PaymentSource = paymentSource;
        PaymentId = paymentId;
        Status = status;
        PayUrl = payUrl;
        CreatedAtUtc = createdAtUtc;
    }
}
