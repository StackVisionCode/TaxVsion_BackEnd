using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>
/// Correlación de la factura con su ancla ESTABLE de cobro en PaymentClient (el <c>PayableReference</c>).
/// La <see cref="CheckoutUrl"/> es la URL estable/no-expira que se embebe en el PDF; el link con token
/// se acuña perezosamente del lado PaymentClient al abrirse. Entidad normal (no owned) porque la Fase 3
/// necesita buscarla por <see cref="ExternalPayableId"/>, transicionar estados, indexar y auditar.
/// </summary>
public sealed class InvoicePaymentLink : BaseEntity
{
    public Guid InvoiceId { get; private set; }

    /// <summary>Id del <c>PayableReference</c> en PaymentClient (dueño del cobro). Clave de correlación
    /// con los eventos payments.* en Fase 3.</summary>
    public Guid ExternalPayableId { get; private set; }

    /// <summary>URL estable pública de cobro (la del botón/QR del PDF). No expira.</summary>
    public string CheckoutUrl { get; private set; } = string.Empty;

    public InvoicePaymentLinkStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Vencimiento del ancla, si aplica. Para el payable estable es null (no expira); el campo
    /// existe para correlacionar links con token en Fase 3.</summary>
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    private InvoicePaymentLink() { }

    internal InvoicePaymentLink(Guid invoiceId, Guid externalPayableId, string checkoutUrl, DateTime createdAtUtc)
    {
        InvoiceId = invoiceId;
        ExternalPayableId = externalPayableId;
        CheckoutUrl = checkoutUrl;
        Status = InvoicePaymentLinkStatus.Active;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Marca este enlace como reemplazado por otro (Fase 3). No-op si ya no está Active.</summary>
    internal void Supersede(DateTime nowUtc)
    {
        if (Status == InvoicePaymentLinkStatus.Active)
            Status = InvoicePaymentLinkStatus.Superseded;
    }
}
