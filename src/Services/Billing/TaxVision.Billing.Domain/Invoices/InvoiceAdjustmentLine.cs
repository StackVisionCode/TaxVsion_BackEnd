using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>Línea de ajuste (descuento) de una factura. Entidad interna del aggregate <see cref="Invoice"/>.
/// A diferencia de <see cref="InvoiceLineItem"/> (cargo positivo), representa una REDUCCIÓN del total:
/// un código promocional, un beneficio de referido o un gift aplicado en el onboarding. Cada ajuste
/// referencia opcionalmente su reserva en Growth (<see cref="GrowthReservationId"/>) para conciliación.
/// <see cref="AmountCents"/> es la MAGNITUD del descuento (positiva); se renderiza en negativo.</summary>
public sealed class InvoiceAdjustmentLine : BaseEntity
{
    public Guid InvoiceId { get; private set; }
    public InvoiceAdjustmentType Type { get; private set; }
    public string? Code { get; private set; }
    public Guid? GrowthReservationId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }

    private InvoiceAdjustmentLine() { }

    internal InvoiceAdjustmentLine(
        Guid invoiceId,
        InvoiceAdjustmentType type,
        string? code,
        Guid? growthReservationId,
        Money amount,
        DateTime createdAtUtc
    )
    {
        InvoiceId = invoiceId;
        Type = type;
        Code = code;
        GrowthReservationId = growthReservationId;
        Amount = amount;
        CreatedAtUtc = createdAtUtc;
    }
}
