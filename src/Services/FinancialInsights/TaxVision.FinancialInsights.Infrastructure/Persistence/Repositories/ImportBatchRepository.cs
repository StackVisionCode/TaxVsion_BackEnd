using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Infrastructure.Persistence.Repositories;

// IgnoreQueryFilters + tenant explícito: corre igual en requests, consumers y jobs.
public sealed class ImportBatchRepository(FinancialInsightsDbContext db) : IImportBatchRepository
{
    public Task<ImportBatch?> GetAsync(Guid tenantId, Guid batchId, CancellationToken ct = default) =>
        Batches().SingleOrDefaultAsync(b => b.TenantId == tenantId && b.Id == batchId, ct);

    public Task<ImportBatch?> GetForCustomerAsync(
        Guid tenantId,
        Guid customerId,
        Guid batchId,
        CancellationToken ct = default
    ) => Batches().SingleOrDefaultAsync(b => b.TenantId == tenantId && b.CustomerId == customerId && b.Id == batchId, ct);

    public Task<ImportBatch?> GetByFileAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        Batches().SingleOrDefaultAsync(b => b.TenantId == tenantId && b.Documents.Any(d => d.FileId == fileId), ct);

    public Task<ImportBatch?> GetByDocumentAsync(Guid tenantId, Guid documentId, CancellationToken ct = default) =>
        Batches().SingleOrDefaultAsync(b => b.TenantId == tenantId && b.Documents.Any(d => d.Id == documentId), ct);

    public Task<bool> AnyFileRegisteredAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken ct = default
    ) =>
        db.ImportDocuments.IgnoreQueryFilters()
            .AnyAsync(d => d.TenantId == tenantId && fileIds.Contains(d.FileId), ct);

    public async Task<bool> TryAddAsync(ImportBatch batch, CancellationToken ct = default)
    {
        db.ImportBatches.Add(batch);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (FinancialInsightsDbContext.IsUniqueViolation(ex))
        {
            // Otro lote ganó el archivo: se suelta este para que el commit final no lo reintente.
            foreach (var entry in ex.Entries)
                entry.State = EntityState.Detached;
            db.Entry(batch).State = EntityState.Detached;
            foreach (var document in batch.Documents)
                db.Entry(document).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<PagedResult<ImportBatchSummaryView>> ListAsync(
        ImportBatchListFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = db.ImportBatches.IgnoreQueryFilters().AsNoTracking().Where(b => b.TenantId == filter.TenantId);
        if (filter.CustomerId is { } customerId)
            query = query.Where(b => b.CustomerId == customerId);
        if (filter.Status is { } status)
            query = query.Where(b => b.Documents.Any(d => d.Status == status));
        if (filter.AssignedToUserId is { } userId)
            query = query.Where(b =>
                db.Set<CustomerAssignmentProjection>()
                    .IgnoreQueryFilters()
                    .Any(a => a.TenantId == filter.TenantId && a.UserId == userId && a.CustomerId == b.CustomerId)
            );

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new
            {
                b.Id,
                b.CustomerId,
                b.CreatedBy,
                b.CreatedAtUtc,
                Total = b.Documents.Count(),
                AwaitingScan = b.Documents.Count(d => d.Status == ImportDocumentStatus.AwaitingScan),
                Ready = b.Documents.Count(d => d.Status == ImportDocumentStatus.Ready),
                Processing = b.Documents.Count(d => d.Status == ImportDocumentStatus.Processing),
                Completed = b.Documents.Count(d => d.Status == ImportDocumentStatus.Completed),
                NeedsReview = b.Documents.Count(d => d.Status == ImportDocumentStatus.NeedsReview),
                Rejected = b.Documents.Count(d => d.Status == ImportDocumentStatus.Rejected),
                Failed = b.Documents.Count(d => d.Status == ImportDocumentStatus.Failed),
            })
            .ToListAsync(ct);

        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToArray();
        var names = await db
            .CustomerDirectoryProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == filter.TenantId && customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId, c => c.DisplayName, ct);

        var items = rows.Select(r => new ImportBatchSummaryView(
                r.Id,
                r.CustomerId,
                names.GetValueOrDefault(r.CustomerId),
                r.CreatedBy,
                r.CreatedAtUtc,
                new ImportDocumentCountsView(
                    r.Total,
                    r.AwaitingScan,
                    r.Ready,
                    r.Processing,
                    r.Completed,
                    r.NeedsReview,
                    r.Rejected,
                    r.Failed
                )
            ))
            .ToArray();
        return new PagedResult<ImportBatchSummaryView>(items, page, pageSize, total);
    }

    private IQueryable<ImportBatch> Batches() => db.ImportBatches.IgnoreQueryFilters().Include(b => b.Documents);
}
