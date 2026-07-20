using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Legal;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

public sealed class FileObjectRepository(CloudStorageDbContext db) : IFileObjectRepository
{
    public void Add(FileObject file) => db.Files.Add(file);

    public void Remove(FileObject file) => db.Files.Remove(file);

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

    public async Task<IReadOnlyList<FileObject>> ListSoftDeletedAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.AsNoTracking()
            .Where(file => file.TenantId == tenantId && file.Status == FileStatus.SoftDeleted)
            .OrderByDescending(file => file.SoftDeletedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FileObject>> ListPurgeablePastRetentionAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.Where(file => file.Status == FileStatus.SoftDeleted && file.SoftDeleteExpiresAtUtc <= nowUtc)
            .OrderBy(file => file.SoftDeleteExpiresAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FileObject>> ListInFolderAsync(
        Guid tenantId,
        Guid? folderId,
        Guid? restrictedCustomerId,
        OwnerType? ownerType,
        Guid? ownerId,
        CancellationToken ct
    ) =>
        await db
            .Files.AsNoTracking()
            .Where(file =>
                file.TenantId == tenantId
                && file.FolderId == folderId
                && file.Status != FileStatus.SoftDeleted
                && (
                    restrictedCustomerId == null
                    || (file.OwnerType == OwnerType.Customer && file.OwnerId == restrictedCustomerId)
                )
                // Filtro adicional solo-staff (2026-07-20) — jamas se evalua si restrictedCustomerId
                // ya acoto el alcance arriba, portal de cliente siempre gana.
                && (restrictedCustomerId != null || ownerType == null || file.OwnerType == ownerType)
                && (restrictedCustomerId != null || ownerId == null || file.OwnerId == ownerId)
            )
            .OrderByDescending(file => file.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FileObject>> ListInFoldersAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> folderIds,
        Guid? restrictedCustomerId,
        CancellationToken ct
    ) =>
        await db
            .Files.AsNoTracking()
            .Where(file =>
                file.TenantId == tenantId
                && file.FolderId != null
                && folderIds.Contains(file.FolderId.Value)
                && file.Status != FileStatus.SoftDeleted
                && (
                    restrictedCustomerId == null
                    || (file.OwnerType == OwnerType.Customer && file.OwnerId == restrictedCustomerId)
                )
            )
            .ToListAsync(ct);
}

public sealed class FolderRepository(CloudStorageDbContext db) : IFolderRepository
{
    public void Add(Folder folder) => db.Folders.Add(folder);

    public void Remove(Folder folder) => db.Folders.Remove(folder);

    public Task<Folder?> GetAsync(Guid tenantId, Guid folderId, CancellationToken ct) =>
        db.Folders.SingleOrDefaultAsync(folder => folder.TenantId == tenantId && folder.Id == folderId, ct);

    public async Task<IReadOnlyList<Folder>> ListSubfoldersAsync(
        Guid tenantId,
        Guid? parentFolderId,
        Guid? restrictedCustomerId,
        OwnerType? ownerType,
        Guid? ownerId,
        CancellationToken ct
    ) =>
        await db
            .Folders.AsNoTracking()
            .Where(folder =>
                folder.TenantId == tenantId
                && folder.ParentFolderId == parentFolderId
                && (
                    restrictedCustomerId == null
                    || (folder.OwnerType == OwnerType.Customer && folder.OwnerId == restrictedCustomerId)
                )
                && (restrictedCustomerId != null || ownerType == null || folder.OwnerType == ownerType)
                && (restrictedCustomerId != null || ownerId == null || folder.OwnerId == ownerId)
            )
            .OrderBy(folder => folder.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Folder>> ListByPathPrefixAsync(
        Guid tenantId,
        string relativePathPrefix,
        CancellationToken ct
    )
    {
        var prefix = relativePathPrefix + "/";
        return await db
            .Folders.Where(folder => folder.TenantId == tenantId && folder.RelativePath.StartsWith(prefix))
            .ToListAsync(ct);
    }

    public Task<bool> NameExistsUnderParentAsync(
        Guid tenantId,
        Guid? parentFolderId,
        string name,
        OwnerType ownerType,
        Guid? ownerId,
        Guid? excludeFolderId,
        CancellationToken ct
    ) =>
        db.Folders.AnyAsync(
            folder =>
                folder.TenantId == tenantId
                && folder.ParentFolderId == parentFolderId
                && folder.Name == name
                && folder.OwnerType == ownerType
                && folder.OwnerId == ownerId
                && folder.Id != (excludeFolderId ?? Guid.Empty),
            ct
        );

    public Task<Folder?> GetByOwnerAndCategoryAsync(
        Guid tenantId,
        OwnerType ownerType,
        Guid? ownerId,
        string category,
        CancellationToken ct
    ) =>
        db.Folders.SingleOrDefaultAsync(
            folder =>
                folder.TenantId == tenantId
                && folder.OwnerType == ownerType
                && folder.OwnerId == ownerId
                && folder.Category == category,
            ct
        );

    public async Task<IReadOnlyList<Folder>> ListAllForOwnerScopeAsync(
        Guid tenantId,
        OwnerType? ownerType,
        Guid? ownerId,
        CancellationToken ct
    ) =>
        await db
            .Folders.AsNoTracking()
            .Where(folder =>
                folder.TenantId == tenantId
                && (ownerType == null || folder.OwnerType == ownerType)
                && (ownerId == null || folder.OwnerId == ownerId)
            )
            .ToListAsync(ct);
}

public sealed class StorageLimitRepository(CloudStorageDbContext db) : IStorageLimitRepository
{
    public void Add(TenantStorageLimit limit) => db.StorageLimits.Add(limit);

    public Task<TenantStorageLimit?> GetAsync(Guid tenantId, CancellationToken ct) =>
        db.StorageLimits.SingleOrDefaultAsync(limit => limit.TenantId == tenantId, ct);
}

/// <summary>Fase C3 — links de compartir. GetByTokenHashAsync siempre trae los Recipients: la resolucion de acceso los necesita.</summary>
public sealed class ShareLinkRepository(CloudStorageDbContext db) : IShareLinkRepository
{
    public void Add(ShareLink link) => db.ShareLinks.Add(link);

    public Task<ShareLink?> GetAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        db.ShareLinks.SingleOrDefaultAsync(link => link.TenantId == tenantId && link.Id == id, ct);

    public Task<ShareLink?> GetByTokenHashAsync(byte[] tokenHash, CancellationToken ct) =>
        db.ShareLinks.Include(link => link.Recipients).SingleOrDefaultAsync(link => link.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<ShareLink>> ListForResourceAsync(
        Guid tenantId,
        Guid resourceId,
        ShareResourceType resourceType,
        CancellationToken ct
    ) =>
        await db
            .ShareLinks.AsNoTracking()
            .Where(link =>
                link.TenantId == tenantId && link.ResourceId == resourceId && link.ResourceType == resourceType
            )
            .OrderByDescending(link => link.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ShareLink>> ListSharedWithUserAsync(
        Guid tenantId,
        Guid userId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .ShareLinks.AsNoTracking()
            .Where(link =>
                link.TenantId == tenantId
                && link.Status == ShareStatus.Active
                && (
                    link.Visibility == ShareVisibility.TenantOnly
                    || (
                        link.Visibility == ShareVisibility.SpecificUsers
                        && link.Recipients.Any(r => r.Kind == ShareRecipientKind.User && r.RecipientUserId == userId)
                    )
                )
            )
            .OrderByDescending(link => link.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ShareLink>> ListSharedWithCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .ShareLinks.AsNoTracking()
            .Where(link =>
                link.TenantId == tenantId
                && link.Status == ShareStatus.Active
                && link.Visibility == ShareVisibility.TenantCustomers
                && (
                    !link.Recipients.Any()
                    || link.Recipients.Any(r =>
                        r.Kind == ShareRecipientKind.Customer && r.RecipientCustomerId == customerId
                    )
                )
            )
            .OrderByDescending(link => link.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ShareLink>> ListActivePublicFolderSharesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> folderIds,
        CancellationToken ct
    ) =>
        await db
            .ShareLinks.AsNoTracking()
            .Where(link =>
                link.TenantId == tenantId
                && link.Status == ShareStatus.Active
                && link.ResourceType == ShareResourceType.Folder
                && link.Visibility == ShareVisibility.Public
                && folderIds.Contains(link.ResourceId)
            )
            .ToListAsync(ct);
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

/// <summary>Fase L1.3 — expedientes DMCA (ver Domain/Legal/DmcaNotice.cs).</summary>
public sealed class DmcaNoticeRepository(CloudStorageDbContext db) : IDmcaNoticeRepository
{
    private static readonly DmcaNoticeStatus[] ActiveStatuses =
    [
        DmcaNoticeStatus.Received,
        DmcaNoticeStatus.CounterNoticeSubmitted,
    ];

    public void Add(DmcaNotice notice) => db.DmcaNotices.Add(notice);

    public Task<DmcaNotice?> GetAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        db.DmcaNotices.SingleOrDefaultAsync(notice => notice.TenantId == tenantId && notice.Id == id, ct);

    public Task<bool> HasActiveNoticeForFileAsync(Guid tenantId, Guid fileId, CancellationToken ct) =>
        db.DmcaNotices.AnyAsync(
            notice => notice.TenantId == tenantId && notice.FileId == fileId && ActiveStatuses.Contains(notice.Status),
            ct
        );
}
