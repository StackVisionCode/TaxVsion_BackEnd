namespace TaxVision.Billing.Domain.Invoices;

/// <summary>
/// Resultado de <see cref="Invoice.OverrideStatus"/>: qué pagos se revirtieron (para avisar que un cobro online hay que
/// reembolsarlo en el proveedor) y si el propio cambio registró un pago manual por el saldo (al marcar como pagada).
/// </summary>
public sealed record StatusOverrideOutcome(
    bool Changed,
    IReadOnlyList<InvoicePayment> ReversedPayments,
    bool PaymentRecorded
)
{
    public static readonly StatusOverrideOutcome NoChange = new(false, [], false);

    /// <summary>Pagos revertidos que movieron dinero real por un proveedor: la oficina debe devolverlos allí.</summary>
    public IReadOnlyList<InvoicePayment> RefundsDueAtProvider =>
        [.. ReversedPayments.Where(p => p.NeedsProviderRefund)];
}
