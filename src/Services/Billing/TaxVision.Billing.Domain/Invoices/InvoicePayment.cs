using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>Un pago aplicado a la factura. Append-only; AmountPaid es la suma de estas filas.
/// (Source, SourceReference) es la clave de idempotencia por pago.</summary>
public sealed class InvoicePayment : BaseEntity
{
    public const int ReferenceMaxLength = 128;

    public Guid InvoiceId { get; private set; }
    public InvoicePaymentSource Source { get; private set; }
    public string SourceReference { get; private set; } = string.Empty;
    public Money Amount { get; private set; } = null!;
    public PaymentMethod Method { get; private set; }
    public DateTime PaidAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid RecordedByUserId { get; private set; }

    /// <summary>Parte de este pago que excede el total de la factura (sobrepago visible).</summary>
    public long ExceedsDueCents { get; private set; }

    private InvoicePayment() { }

    internal InvoicePayment(
        Guid invoiceId,
        InvoicePaymentSource source,
        string sourceReference,
        Money amount,
        PaymentMethod method,
        DateTime paidAtUtc,
        Guid recordedByUserId,
        long exceedsDueCents
    )
    {
        InvoiceId = invoiceId;
        Source = source;
        SourceReference = sourceReference;
        Amount = amount;
        Method = method;
        PaidAtUtc = paidAtUtc;
        RecordedByUserId = recordedByUserId;
        ExceedsDueCents = exceedsDueCents;
    }
}

/// <summary>Origen del pago. Se persiste como texto.</summary>
public enum InvoicePaymentSource
{
    Online,
    Manual,
    CarriedCredit,
    Legacy,
}
