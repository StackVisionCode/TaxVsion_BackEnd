using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>
/// Rastro de auditoría (item 6.2): una fila por cada transición de estado de la factura — quién, cuándo,
/// de qué estado a cuál y por qué (razón + disparador). Entidad NORMAL (no owned), hija de la factura:
/// se busca/lista por InvoiceId, es append-only (nunca se edita ni borra) y sobrevive a la factura.
/// El dominio la escribe desde cada método de transición (Issue/MarkPaid/Void/ChangeStatus) — así el
/// historial es completo sin depender de que cada handler se acuerde de auditar.
/// </summary>
public sealed class InvoiceStatusChange : BaseEntity
{
    public Guid InvoiceId { get; private set; }

    /// <summary>Estado previo. Null solo en la fila de creación (nace en From=null → To=Draft/Paid).</summary>
    public InvoiceStatus? FromStatus { get; private set; }
    public InvoiceStatus ToStatus { get; private set; }

    /// <summary>Qué originó la transición: Created / Issue / Payment / Void / ManualChange. Distingue una
    /// transición automática (cobro, emisión) de un cambio manual hecho por un usuario autorizado.</summary>
    public string Trigger { get; private set; } = string.Empty;

    /// <summary>Motivo opcional (obligatorio de facto en anulaciones/cambios manuales según el caller).</summary>
    public string? Reason { get; private set; }

    /// <summary>Actor que causó la transición. Guid.Empty en transiciones de sistema (onboarding).</summary>
    public Guid ChangedByUserId { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }

    private InvoiceStatusChange() { }

    internal InvoiceStatusChange(
        Guid invoiceId,
        InvoiceStatus? fromStatus,
        InvoiceStatus toStatus,
        string trigger,
        string? reason,
        Guid changedByUserId,
        DateTime changedAtUtc
    )
    {
        InvoiceId = invoiceId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Trigger = trigger;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ChangedByUserId = changedByUserId;
        ChangedAtUtc = changedAtUtc;
    }
}

/// <summary>Disparadores del rastro de auditoría de estado. Strings estables (se persisten como texto).</summary>
public static class StatusChangeTrigger
{
    public const string Created = "Created";
    public const string Issue = "Issue";
    public const string Payment = "Payment";
    public const string Void = "Void";
    public const string ManualChange = "ManualChange";
}
