using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence.Configurations;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

/// <summary>Pendiente y vencida del aging, compartidas por el dashboard y el listado para que no diverjan.</summary>
internal static class OpenInvoiceRules
{
    // Pendiente = emitida, enviada o con pago parcial y saldo.
    public static IQueryable<Invoice> WhereOpen(this IQueryable<Invoice> invoices) =>
        invoices
            .Where(i =>
                i.Status == InvoiceStatus.Issued
                || i.Status == InvoiceStatus.Sent
                || i.Status == InvoiceStatus.PartiallyPaid
            )
            .Where(i => EF.Property<long>(i, InvoiceCentsColumns.AmountDue) > 0);

    // Base = vencimiento; si no lo tiene (valor por defecto, anterior a la emisión), la emisión.
    public static IQueryable<Invoice> WhereBaseBefore(this IQueryable<Invoice> invoices, DateTime cutoffUtc) =>
        invoices.Where(i => (i.DueDateUtc >= i.IssueDateUtc ? i.DueDateUtc : i.IssueDateUtc) < cutoffUtc);
}
