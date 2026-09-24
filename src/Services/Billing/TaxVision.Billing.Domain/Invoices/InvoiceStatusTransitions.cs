using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices;

/// <summary>
/// Matriz EXPLÍCITA de transiciones legales de estado de una factura (item 6.2). Única fuente de verdad
/// del ciclo de vida: reemplaza cualquier <c>ChangeStatus(any→any)</c> genérico. Solo se listan aquí las
/// transiciones que un usuario autorizado puede aplicar MANUALMENTE (endpoint <c>/status</c>); las que
/// tienen efectos colaterales tienen su propio comando dedicado y NO pasan por acá:
/// <list type="bullet">
/// <item>Draft → Issued: <c>Issue</c> (numeración server-side + descuento de inventario).</item>
/// <item>* → Paid/PartiallyPaid: <c>record-payment</c> / cobro online (recibo + hash).</item>
/// <item>* → Voided: <c>void</c> (repone stock + revoca el enlace de pago).</item>
/// </list>
/// Lo único que faltaba en la máquina de estados manual era marcar/desmarcar "Sent" (enviada al cliente),
/// un flip puro sin efectos colaterales — que es lo que esta matriz habilita.
/// </summary>
public static class InvoiceStatusTransitions
{
    /// <summary>Transiciones manuales permitidas: (estado actual) → conjunto de destinos legales.</summary>
    private static readonly IReadOnlyDictionary<InvoiceStatus, IReadOnlySet<InvoiceStatus>> Allowed =
        new Dictionary<InvoiceStatus, IReadOnlySet<InvoiceStatus>>
        {
            // Emitida ⇄ Enviada: marcar como enviada al cliente (o revertir si no se envió).
            [InvoiceStatus.Issued] = new HashSet<InvoiceStatus> { InvoiceStatus.Sent },
            [InvoiceStatus.Sent] = new HashSet<InvoiceStatus> { InvoiceStatus.Issued },
        };

    /// <summary>¿Es legal mover una factura de <paramref name="from"/> a <paramref name="to"/> manualmente?
    /// Mover al mismo estado se considera legal (no-op idempotente).</summary>
    public static bool IsLegal(InvoiceStatus from, InvoiceStatus to) =>
        from == to || (Allowed.TryGetValue(from, out var set) && set.Contains(to));

    /// <summary>Destinos manuales legales desde <paramref name="from"/> (sin incluir el mismo estado).
    /// El frontend lo usa para ofrecer solo los cambios de estado válidos.</summary>
    public static IReadOnlyList<InvoiceStatus> AllowedTargets(InvoiceStatus from) =>
        Allowed.TryGetValue(from, out var set) ? set.ToList() : [];
}
