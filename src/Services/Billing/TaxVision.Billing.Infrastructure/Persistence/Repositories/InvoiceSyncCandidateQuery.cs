using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

/// <summary>Prefiltro SQL del job de reconciliación (BIL-3b). Cruza tenants a propósito: IgnoreQueryFilters.</summary>
public sealed class InvoiceSyncCandidateQuery(BillingDbContext dbContext) : IInvoiceSyncCandidateQuery
{
    private readonly BillingDbContext _dbContext = dbContext;

    public async Task<IReadOnlyList<Invoice>> ListAsync(
        DateTime notBeforeUtc,
        DateTime updatedBeforeUtc,
        int take,
        CancellationToken ct = default
    ) =>
        await _dbContext
            .Invoices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i =>
                i.DeletedAtUtc == null
                && i.UpdatedAtUtc >= notBeforeUtc
                && i.UpdatedAtUtc < updatedBeforeUtc
                && i.Status != InvoiceStatus.Draft
                && (
                    // Enlace: saldo nunca confirmado o distinto (Money se guarda como texto "cents|CUR").
                    (
                        (
                            i.Status == InvoiceStatus.Issued
                            || i.Status == InvoiceStatus.Sent
                            || i.Status == InvoiceStatus.PartiallyPaid
                        )
                        && i.AmountPaid != i.Total
                        && (i.PaymentLinkSyncedDue == null || i.PaymentLinkSyncedDue != i.AmountDue)
                    )
                    // Stock: versión actual sin reconciliar.
                    || i.StockSyncedAtUtc == null
                    || i.StockSyncedAtUtc < i.UpdatedAtUtc
                )
            )
            .OrderBy(i => i.UpdatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(ct);
}
