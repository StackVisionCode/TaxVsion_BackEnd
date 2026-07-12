using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

public sealed class FileObjectRepository(CloudStorageDbContext db) : IFileObjectRepository
{
    public void Add(FileObject file) => db.Files.Add(file);

    public Task<FileObject?> GetAsync(Guid tenantId, Guid fileId, CancellationToken ct) =>
        db.Files.SingleOrDefaultAsync(file => file.TenantId == tenantId && file.Id == fileId, ct);

    public async Task<IReadOnlyList<FileObject>> ListAsync(
        Guid tenantId,
        Guid? restrictedCustomerId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.AsNoTracking()
            .Where(file =>
                file.TenantId == tenantId
                && file.Status != FileStatus.SoftDeleted
                && (
                    restrictedCustomerId == null
                    || (file.OwnerType == OwnerType.Customer && file.OwnerId == restrictedCustomerId)
                )
            )
            .OrderByDescending(file => file.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FileObject>> ListExpiredUploadsAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.Where(file => file.Status == FileStatus.PendingUpload && file.UploadExpiresAtUtc <= nowUtc)
            .OrderBy(file => file.UploadExpiresAtUtc)
            .Take(take)
            .ToListAsync(ct);
}

public sealed class StorageLimitRepository(CloudStorageDbContext db) : IStorageLimitRepository
{
    public void Add(TenantStorageLimit limit) => db.StorageLimits.Add(limit);

    public Task<TenantStorageLimit?> GetAsync(Guid tenantId, CancellationToken ct) =>
        db.StorageLimits.SingleOrDefaultAsync(limit => limit.TenantId == tenantId, ct);
}

public sealed class StorageAuditRepository(CloudStorageDbContext db) : IStorageAuditRepository
{
    public void Add(StorageAccessLog log) => db.AccessLogs.Add(log);

    public async Task<IReadOnlyList<StorageAccessLog>> ListAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .AccessLogs.AsNoTracking()
            .Where(log => log.TenantId == tenantId)
            .OrderByDescending(log => log.OccurredAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
}
