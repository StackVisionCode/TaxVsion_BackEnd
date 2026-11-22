using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>Un pago aplicado a la factura. Append-only; AmountPaid es la suma de estas filas.
/// (Source, SourceReference) es la clave de idempotencia por pago.</summary>
public sealed class InvoicePayment : BaseEntity
{
    public const int ReferenceMaxLength = 128;

    public Guid InvoiceId { get; private set; }

    /// <summary>Oficina de la factura, copiada al crear el pago: los totales filtran pagos sin unir con facturas.</summary>
    public Guid TenantId { get; private set; }

    public InvoicePaymentSource Source { get; private set; }
    public string SourceReference { get; private set; } = string.Empty;
    public Money Amount { get; private set; } = null!;
    public PaymentMethod Method { get; private set; }
    public DateTime PaidAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid RecordedByUserId { get; private set; }

    /// <summary>Parte de este pago que excede el total de la factura (sobrepago visible).</summary>
    public long ExceedsDueCents { get; private set; }

    /// <summary>Cuándo la oficina revirtió este pago al devolver la factura a un estado sin cobrar. El pago NO se borra
    /// (queda en el historial) pero ya no cuenta para lo pagado. null = vigente.</summary>
    public DateTime? ReversedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? ReversedByUserId { get; private set; }

    public string? ReversalReason { get; private set; }

    /// <summary>True si el pago sigue contando para lo pagado.</summary>
    public bool IsActive => ReversedAtUtc is null;

    /// <summary>Un cobro online (Stripe, etc.) mueve dinero real: revertirlo aquí NO lo devuelve al cliente, hay que
    /// reembolsarlo en el proveedor. Los manuales y el crédito arrastrado no. Un pago <c>Legacy</c> cuenta si la
    /// factura se cobró en línea.</summary>
    public bool NeedsProviderRefund =>
        Source == InvoicePaymentSource.Online
        || (Source == InvoicePaymentSource.Legacy && Method == PaymentMethod.Online);

    /// <summary>Crédito arrastrado de la factura reemplazada: ese dinero ya se cobró allí. Revertirlo no lo devuelve
    /// ni lo guarda: queda sin aplicar hasta que la oficina lo reembolse o lo vuelva a aplicar.</summary>
    public bool IsCarriedCredit => Source == InvoicePaymentSource.CarriedCredit;

    private InvoicePayment() { }

    /// <summary>Marca el pago como revertido. Idempotente: revertir de nuevo no cambia la fecha ni el motivo originales.</summary>
    internal void Reverse(string reason, DateTime nowUtc, Guid actorUserId)
    {
        if (ReversedAtUtc is not null)
            return;
        ReversedAtUtc = nowUtc;
        ReversedByUserId = actorUserId;
        ReversalReason = reason;
    }

    internal InvoicePayment(
        Guid invoiceId,
        Guid tenantId,
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
        TenantId = tenantId;
        Source = source;
        SourceReference = sourceReference;
        Amount = amount;
        Method = method;
        PaidAtUtc = paidAtUtc;
        RecordedByUserId = recordedByUserId;
        ExceedsDueCents = exceedsDueCents;
    }
}

/// <summary>Origen del pago. Se persiste como texto. <c>Legacy</c>: lo cobrado antes de que existieran estas filas;
/// solo se escribe, ya revertido, cuando una corrección de estado lo deja en cero.</summary>
public enum InvoicePaymentSource
{
    Online,
    Manual,
    CarriedCredit,
    Legacy,
}
