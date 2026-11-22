using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence.Configurations;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

/// <summary>
/// GROUP BY en vivo sobre la columna calculada <c>Category</c>; las dos lecturas salen del índice
/// <c>IX_Files_Tenant_Status_Category</c> sin tocar la tabla. Dato de oficina: sin recorte por asignación.
/// </summary>
public sealed class StorageBreakdownReader(CloudStorageDbContext db) : IStorageBreakdownReader
{
    public async Task<StorageBreakdown> ReadAsync(Guid tenantId, CancellationToken ct)
    {
        var files = db.Files.IgnoreQueryFilters().AsNoTracking().Where(file => file.TenantId == tenantId);
        var used = await files
            .Where(file => file.Status == FileStatus.Available)
            .GroupBy(file => EF.Property<string>(file, FileObjectConfiguration.CategoryColumn))
            .Select(group => new
            {
                Category = group.Key,
                Count = group.Count(),
                Bytes = group.Sum(file => file.SizeBytes),
            })
            .ToListAsync(ct);
        // Lo que está en la papelera y todavía cuenta como usado (FileObject.FootprintOf).
        var trash = files.Where(file =>
            file.Status == FileStatus.SoftDeleted
            && (file.StatusBeforeSoftDelete == null || file.StatusBeforeSoftDelete == FileStatus.Available)
        );
        var trashCount = await trash.CountAsync(ct);
        var trashBytes = trashCount == 0 ? 0 : await trash.SumAsync(file => file.SizeBytes, ct);
        return new StorageBreakdown(
            StorageCategories
                .All.Select(category =>
                    used.SingleOrDefault(row => row.Category == category) is { } row
                        ? new CategoryUsage(category, row.Count, row.Bytes)
                        : new CategoryUsage(category, 0, 0)
                )
                .ToList(),
            new TrashUsage(trashCount, trashBytes)
        );
    }
}
