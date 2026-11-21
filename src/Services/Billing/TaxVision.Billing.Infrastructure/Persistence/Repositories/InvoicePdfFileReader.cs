using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

/// <summary>IgnoreQueryFilters + tenant explícito: el comando corre en un scope de Wolverine sin tenant ambiental.</summary>
public sealed class InvoicePdfFileReader(BillingDbContext dbContext) : IInvoicePdfFileReader
{
    public async Task<IReadOnlyList<InvoicePdfFileRow>> ListPageAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken ct = default
    ) =>
        await dbContext
            .Invoices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i =>
                i.TenantId == tenantId && i.DeletedAtUtc == null && (i.PdfFileId != null || i.PaidPdfFileId != null)
            )
            .OrderBy(i => i.Id)
            .Skip(skip)
            .Take(take)
            .Select(i => new InvoicePdfFileRow(i.Id, i.CustomerId, i.PdfFileId, i.PaidPdfFileId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InvoicePdfFileRow>> ListByFileIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken ct = default
    ) =>
        await dbContext
            .Invoices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i =>
                i.TenantId == tenantId
                && i.DeletedAtUtc == null
                && (
                    (i.PdfFileId != null && fileIds.Contains(i.PdfFileId.Value))
                    || (i.PaidPdfFileId != null && fileIds.Contains(i.PaidPdfFileId.Value))
                )
            )
            .Select(i => new InvoicePdfFileRow(i.Id, i.CustomerId, i.PdfFileId, i.PaidPdfFileId))
            .ToListAsync(ct);
}
