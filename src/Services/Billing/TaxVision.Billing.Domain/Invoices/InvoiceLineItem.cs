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

    /// <summary>Referencia DÉBIL (sin FK cross-service) al ítem del catálogo del que se snapshoteó esta
    /// línea, si vino de Catalog. Solo trazabilidad — el precio/desc quedan congelados acá igual.</summary>
    public Guid? CatalogItemId { get; private set; }

    private InvoiceLineItem() { }

    // NOTE (scaffold B1): fábrica y validación completas se implementan en B2.
    internal InvoiceLineItem(
        Guid invoiceId,
        string description,
        int quantity,
        Money unitAmount,
        int taxBasisPoints,
        Money taxAmount,
        Money lineTotal,
        Guid? catalogItemId = null
    )
    {
        InvoiceId = invoiceId;
        Description = description;
        Quantity = quantity;
        UnitAmount = unitAmount;
        TaxBasisPoints = taxBasisPoints;
        TaxAmount = taxAmount;
        LineTotal = lineTotal;
        CatalogItemId = catalogItemId;
    }
}
