using TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;
using TaxVision.Billing.Application.Invoices.SyncInvoiceStock;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Application.Invoices.SyncReconciliation;

/// <summary>Ventana del job: solo facturas cambiadas desde <paramref name="NotBeforeUtc"/> y hace más de
/// <paramref name="Grace"/> (las recientes las cubre todavía su propio comando).</summary>
public sealed record InvoiceSyncWindow(DateTime NotBeforeUtc, TimeSpan Grace);

/// <summary>
/// Regla "desalineada" de BIL-3b: el enlace de cobro no tiene el saldo actual, o el stock no se reconcilió
/// para la versión actual de la factura. Devuelve los comandos (idempotentes) que la reparan.
/// </summary>
public static class InvoiceSyncPolicy
{
    public static bool NeedsPaymentLinkSync(Invoice invoice, DateTime nowUtc, InvoiceSyncWindow window) =>
        InWindow(invoice, nowUtc, window)
        && invoice.Status is InvoiceStatus.Issued or InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid
        && invoice.InvoiceNumber is not null
        && invoice.AmountDue.AmountCents > 0
        && invoice.PaymentLinkSyncedDue != invoice.AmountDue;

    public static bool NeedsStockSync(Invoice invoice, DateTime nowUtc, InvoiceSyncWindow window) =>
        InWindow(invoice, nowUtc, window)
        && invoice.Status is not InvoiceStatus.Draft
        && (invoice.StockSyncedAtUtc is null || invoice.StockSyncedAtUtc < invoice.UpdatedAtUtc);

    public static IReadOnlyList<object> PlanFor(Invoice invoice, DateTime nowUtc, InvoiceSyncWindow window)
    {
        var commands = new List<object>(2);
        if (NeedsPaymentLinkSync(invoice, nowUtc, window))
            commands.Add(new EnsureInvoicePaymentLinkCommand(invoice.TenantId, invoice.Id));
        if (NeedsStockSync(invoice, nowUtc, window))
            commands.Add(new SyncInvoiceStockCommand(invoice.TenantId, invoice.Id));
        return commands;
    }

    private static bool InWindow(Invoice invoice, DateTime nowUtc, InvoiceSyncWindow window) =>
        invoice.DeletedAtUtc is null
        && invoice.UpdatedAtUtc >= window.NotBeforeUtc
        && invoice.UpdatedAtUtc <= nowUtc - window.Grace;
}
