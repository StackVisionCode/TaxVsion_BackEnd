using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Folders;
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

    // tenantId ya viene explicito y validado por el caller — sin IgnoreQueryFilters() esta lectura
    // igual queda sujeta al HasQueryFilter fail-closed de EF, que lee TenantContext del scope de DI
    // actual. Para comandos locales despachados por Wolverine (p.ej. ScanFileCommand via
    // bus.PublishAsync), el middleware que popula TenantContext y el handler que resuelve este
    // repositorio pueden correr en scopes de DI distintos, asi que el filtro veria HasTenant=false
    // pese a que el tenant real ya esta disponible como parametro. Mismo patron que
    // ListExpiredUploadsAsync/GetByTokenHashAsync: el parametro explicito ES el limite de
    // autorizacion real, el filtro global es solo defensa en profundidad.
    public Task<FileObject?> GetAsync(Guid tenantId, Guid fileId, CancellationToken ct) =>
        db.Files.IgnoreQueryFilters().SingleOrDefaultAsync(file => file.TenantId == tenantId && file.Id == fileId, ct);

    public async Task<IReadOnlyList<FileObject>> ListAsync(
        Guid tenantId,
        Guid? restrictedCustomerId,
        OwnerType? ownerType,
        Guid? ownerId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file =>
                file.TenantId == tenantId
                && file.Status != FileStatus.SoftDeleted
                && (
                    restrictedCustomerId == null
                    || (file.OwnerType == OwnerType.Customer && file.OwnerId == restrictedCustomerId)
                )
                // ownerType/ownerId: filtro de staff. El portal ya quedo acotado por
                // restrictedCustomerId, asi que ahi estos se ignoran (mismo criterio que ListInFolderAsync).
                && (restrictedCustomerId != null || ownerType == null || file.OwnerType == ownerType)
                && (restrictedCustomerId != null || ownerId == null || file.OwnerId == ownerId)
            )
            .OrderByDescending(file => file.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    // RBAC Fase 5 — cross-tenant por diseño (ExpiredUploadCleanupService recorre TODOS los
    // tenants en cada corrida), único consumidor de este método. IgnoreQueryFilters() explícito.
    public async Task<IReadOnlyList<FileObject>> ListExpiredUploadsAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .Where(file => file.Status == FileStatus.PendingUpload && file.UploadExpiresAtUtc <= nowUtc)
            .OrderBy(file => file.UploadExpiresAtUtc)
            .Take(take)
            .ToListAsync(ct);

    // Backfill (barrido de plataforma): tenants con archivos sin carpeta navegable. Cross-tenant intencional.
    public async Task<IReadOnlyList<Guid>> DistinctTenantsWithUnfiledFilesAsync(
        IReadOnlyCollection<FolderType> navigableTypes,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file =>
                file.FolderId == null
                && file.Status != FileStatus.SoftDeleted
                && navigableTypes.Contains(file.FolderType)
            )
            .Select(file => file.TenantId)
            .Distinct()
            .ToListAsync(ct);

    // Backfill (dry-run): tenantId explicito ES el limite, IgnoreQueryFilters() intencional. AsNoTracking: solo lee.
    public async Task<IReadOnlyList<UnfiledFileGroup>> SummarizeUnfiledFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<FolderType> navigableTypes,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file =>
                file.TenantId == tenantId
                && file.FolderId == null
                && file.Status != FileStatus.SoftDeleted
                && navigableTypes.Contains(file.FolderType)
            )
            .GroupBy(file => new
            {
                file.OwnerType,
                file.OwnerId,
                file.FolderType,
            })
            .Select(g => new UnfiledFileGroup(g.Key.OwnerType, g.Key.OwnerId, g.Key.FolderType, g.Count()))
            .ToListAsync(ct);

    // Backfill (aplicar): TRACKED a proposito — el handler muta FolderId (MoveToFolder) y lo persiste.
    // tenantId explicito ES el limite, IgnoreQueryFilters() intencional.
    public async Task<IReadOnlyList<FileObject>> NextUnfiledFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<FolderType> navigableTypes,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .Where(file =>
                file.TenantId == tenantId
                && file.FolderId == null
                && file.Status != FileStatus.SoftDeleted
                && navigableTypes.Contains(file.FolderType)
            )
            .OrderBy(file => file.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FileObject>> ListSoftDeletedAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file => file.TenantId == tenantId && file.Status == FileStatus.SoftDeleted)
            .OrderByDescending(file => file.SoftDeletedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    // RBAC Fase 5 — cross-tenant por diseño (RecycleBinPurgeService recorre TODOS los tenants
    // en cada corrida diaria), único consumidor de este método. IgnoreQueryFilters() explícito.
    public async Task<IReadOnlyList<FileObject>> ListPurgeablePastRetentionAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .Where(file => file.Status == FileStatus.SoftDeleted && file.SoftDeleteExpiresAtUtc <= nowUtc)
            .OrderBy(file => file.SoftDeleteExpiresAtUtc)
            .Take(take)
            .ToListAsync(ct);

    private IQueryable<FileObject> FilesInFolder(
        Guid tenantId,
        Guid? folderId,
        Guid? restrictedCustomerId,
        OwnerType? ownerType,
        Guid? ownerId,
        FolderContentsFilter filter
    )
    {
        var query = db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
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
            );

        if (filter.FolderTypes is { Count: > 0 } folderTypes)
            query = query.Where(file => folderTypes.Contains(file.FolderType));
        if (filter.TaxYears is { Count: > 0 } years)
            query = query.Where(file => file.TaxYear != null && years.Contains(file.TaxYear.Value));
        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(file => statuses.Contains(file.Status));
        if (filter.Extensions is { Count: > 0 } extensions)
            query = query.Where(BuildExtensionPredicate(extensions));
        return query;
    }

    // OR de EndsWith(".ext") — EF lo traduce a LIKE '%.ext' (colación CI de SQL Server ⇒ no
    // distingue mayúsculas). Se construye a mano para garantizar la traducción con una lista dinámica.
    private static Expression<Func<FileObject, bool>> BuildExtensionPredicate(IReadOnlyList<string> extensions)
    {
        var param = Expression.Parameter(typeof(FileObject), "file");
        var nameProperty = Expression.Property(param, nameof(FileObject.OriginalName));
        var endsWith = typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!;
        Expression? body = null;
        foreach (var extension in extensions)
        {
            var pattern = Expression.Constant("." + extension.TrimStart('.'));
            var call = Expression.Call(nameProperty, endsWith, pattern);
            body = body is null ? call : Expression.OrElse(body, call);
        }
        return Expression.Lambda<Func<FileObject, bool>>(body ?? Expression.Constant(true), param);
    }

    private static IOrderedQueryable<FileObject> OrderFiles(IQueryable<FileObject> query, FolderContentsFilter filter)
    {
        var desc = filter.SortDescending;
        var ordered = filter.SortKey switch
        {
            FileSortKey.Modified => desc
                ? query.OrderByDescending(file => file.ScannedAtUtc ?? file.CreatedAtUtc)
                : query.OrderBy(file => file.ScannedAtUtc ?? file.CreatedAtUtc),
            FileSortKey.Size => desc
                ? query.OrderByDescending(file => file.SizeBytes)
                : query.OrderBy(file => file.SizeBytes),
            _ => desc ? query.OrderByDescending(file => file.OriginalName) : query.OrderBy(file => file.OriginalName),
        };
        // Desempate estable por Id para que la paginación no repita/salte filas con valores iguales.
        return desc ? ordered.ThenByDescending(file => file.Id) : ordered.ThenBy(file => file.Id);
    }

    public async Task<IReadOnlyList<FileObject>> ListInFolderAsync(
        Guid tenantId,
        Guid? folderId,
        Guid? restrictedCustomerId,
        OwnerType? ownerType,
        Guid? ownerId,
        FolderContentsFilter filter,
        int? skip,
        int? take,
        CancellationToken ct
    )
    {
        var query = OrderFiles(
            FilesInFolder(tenantId, folderId, restrictedCustomerId, ownerType, ownerId, filter),
            filter
        );
        return take is null
            ? await query.ToListAsync(ct)
            : await query.Skip(skip ?? 0).Take(take.Value).ToListAsync(ct);
    }

    public Task<int> CountInFolderAsync(
        Guid tenantId,
        Guid? folderId,
        Guid? restrictedCustomerId,
        OwnerType? ownerType,
        Guid? ownerId,
        FolderContentsFilter filter,
        CancellationToken ct
    ) => FilesInFolder(tenantId, folderId, restrictedCustomerId, ownerType, ownerId, filter).CountAsync(ct);

    public async Task<IReadOnlyList<FileObject>> ListInFoldersAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> folderIds,
        Guid? restrictedCustomerId,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
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

    // TRACKED (sin AsNoTracking): el borrado recursivo de carpeta muta el Status de cada archivo.
    public async Task<IReadOnlyList<FileObject>> ListInFoldersForUpdateAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> folderIds,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .Where(file =>
                file.TenantId == tenantId
                && file.FolderId != null
                && folderIds.Contains(file.FolderId.Value)
                && file.Status != FileStatus.SoftDeleted
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FileObject>> ListSoftDeletedLooseAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file =>
                file.TenantId == tenantId && file.Status == FileStatus.SoftDeleted && file.DeletedBatchId == null
            )
            .OrderByDescending(file => file.SoftDeletedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    // TRACKED: la restauración de una carpeta revierte el Status de sus archivos.
    public async Task<IReadOnlyList<FileObject>> ListByDeletedBatchAsync(
        Guid tenantId,
        Guid batchId,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .Where(file => file.TenantId == tenantId && file.DeletedBatchId == batchId)
            .ToListAsync(ct);

    public Task<int> CountByDeletedBatchAsync(Guid tenantId, Guid batchId, CancellationToken ct) =>
        db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(file => file.TenantId == tenantId && file.DeletedBatchId == batchId, ct);
}

public sealed class FolderRepository(CloudStorageDbContext db) : IFolderRepository
{
    public void Add(Folder folder) => db.Folders.Add(folder);

    public void Remove(Folder folder) => db.Folders.Remove(folder);

    public Task<Folder?> GetAsync(Guid tenantId, Guid folderId, CancellationToken ct) =>
        db
            .Folders.IgnoreQueryFilters()
            .SingleOrDefaultAsync(folder => folder.TenantId == tenantId && folder.Id == folderId, ct);

    private IQueryable<Folder> Subfolders(
        Guid tenantId,
        Guid? parentFolderId,
        Guid? restrictedCustomerId,
        OwnerType? ownerType,
        Guid? ownerId
    ) =>
        db
            .Folders.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(folder =>
                folder.TenantId == tenantId
                && folder.ParentFolderId == parentFolderId
                && folder.SoftDeletedAtUtc == null
                && (
                    restrictedCustomerId == null
                    || (folder.OwnerType == OwnerType.Customer && folder.OwnerId == restrictedCustomerId)
                )
                && (restrictedCustomerId != null || ownerType == null || folder.OwnerType == ownerType)
                && (restrictedCustomerId != null || ownerId == null || folder.OwnerId == ownerId)
            );

    public async Task<IReadOnlyList<Folder>> ListSubfoldersAsync(
        Guid tenantId,
        Guid? parentFolderId,
        Guid? restrictedCustomerId,
        OwnerType? ownerType,
        Guid? ownerId,
        FolderContentsFilter filter,
        int? skip,
        int? take,
        CancellationToken ct
    )
    {
        var query = Subfolders(tenantId, parentFolderId, restrictedCustomerId, ownerType, ownerId);
        var desc = filter.SortDescending;
        // Las carpetas no tienen tamaño: "Size" cae a Name. "Modified" ordena por CreatedAtUtc.
        var ordered =
            filter.SortKey == FileSortKey.Modified
                ? (desc ? query.OrderByDescending(f => f.CreatedAtUtc) : query.OrderBy(f => f.CreatedAtUtc))
                : (desc ? query.OrderByDescending(f => f.Name) : query.OrderBy(f => f.Name));
        var stable = desc ? ordered.ThenByDescending(f => f.Id) : ordered.ThenBy(f => f.Id);
        return take is null
            ? await stable.ToListAsync(ct)
            : await stable.Skip(skip ?? 0).Take(take.Value).ToListAsync(ct);
    }

    public Task<int> CountSubfoldersAsync(
        Guid tenantId,
        Guid? parentFolderId,
        Guid? restrictedCustomerId,
        OwnerType? ownerType,
        Guid? ownerId,
        CancellationToken ct
    ) => Subfolders(tenantId, parentFolderId, restrictedCustomerId, ownerType, ownerId).CountAsync(ct);

    public async Task<IReadOnlyList<Folder>> ListByPathPrefixAsync(
        Guid tenantId,
        string relativePathPrefix,
        CancellationToken ct
    )
    {
        var prefix = relativePathPrefix + "/";
        return await db
            .Folders.IgnoreQueryFilters()
            .Where(folder =>
                folder.TenantId == tenantId && folder.SoftDeletedAtUtc == null && folder.RelativePath.StartsWith(prefix)
            )
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
        db
            .Folders.IgnoreQueryFilters()
            .AnyAsync(
                folder =>
                    folder.TenantId == tenantId
                    && folder.ParentFolderId == parentFolderId
                    && folder.Name == name
                    && folder.OwnerType == ownerType
                    && folder.OwnerId == ownerId
                    && folder.SoftDeletedAtUtc == null
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
        db
            .Folders.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                folder =>
                    folder.TenantId == tenantId
                    && folder.OwnerType == ownerType
                    && folder.OwnerId == ownerId
                    && folder.Category == category,
                ct
            );

    // Mismo patron que FileObjectRepository.GetAsync: GetFolderTreeQuery se despacha via
    // bus.InvokeAsync y el handler puede correr en un scope de DI distinto al que populo
    // TenantContext. tenantId ya viene explicito y validado desde el JWT del controller.
    public async Task<IReadOnlyList<Folder>> ListAllForOwnerScopeAsync(
        Guid tenantId,
        OwnerType? ownerType,
        Guid? ownerId,
        CancellationToken ct
    ) =>
        await db
            .Folders.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(folder =>
                folder.TenantId == tenantId
                && folder.SoftDeletedAtUtc == null
                && (ownerType == null || folder.OwnerType == ownerType)
                && (ownerId == null || folder.OwnerId == ownerId)
            )
            .ToListAsync(ct);

    // ---------- Papelera de carpetas (soft-delete por batch) ----------

    // Raíces borradas (DeletedBatchId == Id) → una entrada por carpeta en la papelera.
    public async Task<IReadOnlyList<Folder>> ListSoftDeletedRootsAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Folders.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(folder =>
                folder.TenantId == tenantId && folder.SoftDeletedAtUtc != null && folder.DeletedBatchId == folder.Id
            )
            .OrderByDescending(folder => folder.SoftDeletedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    // Todas las carpetas de un batch (TRACKED: restaurar/purgar las muta o elimina).
    public async Task<IReadOnlyList<Folder>> ListBatchAsync(Guid tenantId, Guid batchId, CancellationToken ct) =>
        await db
            .Folders.IgnoreQueryFilters()
            .Where(folder => folder.TenantId == tenantId && folder.DeletedBatchId == batchId)
            .ToListAsync(ct);

    // Purga: raíces cuya retención venció (cross-tenant, lo corre el job diario). TRACKED para Remove del batch.
    public async Task<IReadOnlyList<Folder>> ListPurgeableRootsPastRetentionAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Folders.IgnoreQueryFilters()
            .Where(folder =>
                folder.SoftDeletedAtUtc != null
                && folder.DeletedBatchId == folder.Id
                && folder.SoftDeleteExpiresAtUtc <= nowUtc
            )
            .OrderBy(folder => folder.SoftDeleteExpiresAtUtc)
            .Take(take)
            .ToListAsync(ct);
}

public sealed class StorageLimitRepository(CloudStorageDbContext db) : IStorageLimitRepository
{
    public void Add(TenantStorageLimit limit) => db.StorageLimits.Add(limit);

    public Task<TenantStorageLimit?> GetAsync(Guid tenantId, CancellationToken ct) =>
        db.StorageLimits.IgnoreQueryFilters().SingleOrDefaultAsync(limit => limit.TenantId == tenantId, ct);
}

/// <summary>Fase C3 — links de compartir. GetByTokenHashAsync siempre trae los Recipients: la resolucion de acceso los necesita.</summary>
public sealed class ShareLinkRepository(CloudStorageDbContext db) : IShareLinkRepository
{
    public void Add(ShareLink link) => db.ShareLinks.Add(link);

    public Task<ShareLink?> GetAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        db.ShareLinks.IgnoreQueryFilters().SingleOrDefaultAsync(link => link.TenantId == tenantId && link.Id == id, ct);

    // RBAC Fase 5 — resolución pública del token (PublicShareController), sin tenant en contexto
    // por diseño (el token en sí es la autorización); IgnoreQueryFilters() explícito.
    public Task<ShareLink?> GetByTokenHashAsync(byte[] tokenHash, CancellationToken ct) =>
        db
            .ShareLinks.IgnoreQueryFilters()
            .Include(link => link.Recipients)
            .SingleOrDefaultAsync(link => link.TokenHash == tokenHash, ct);

    // Tracked (sin AsNoTracking): el consumer de offboard los revoca y persiste. IgnoreQueryFilters
    // porque corre system-level (sin tenant en contexto); el WHERE acota por TenantId explícito.
    public async Task<IReadOnlyList<ShareLink>> ListActiveByCreatorAsync(
        Guid tenantId,
        Guid createdByUserId,
        CancellationToken ct
    ) =>
        await db
            .ShareLinks.IgnoreQueryFilters()
            .Where(link =>
                link.TenantId == tenantId
                && link.CreatedByUserId == createdByUserId
                && link.Status == ShareStatus.Active
            )
            .ToListAsync(ct);

    // Mismo filtro que ListActiveByCreatorAsync (activos del creador), solo cuenta — pre-flight de impacto.
    public Task<int> CountActiveByCreatorAsync(Guid tenantId, Guid createdByUserId, CancellationToken ct) =>
        db
            .ShareLinks.IgnoreQueryFilters()
            .CountAsync(
                link =>
                    link.TenantId == tenantId
                    && link.CreatedByUserId == createdByUserId
                    && link.Status == ShareStatus.Active,
                ct
            );

    public async Task<IReadOnlyList<ShareLink>> ListForResourceAsync(
        Guid tenantId,
        Guid resourceId,
        ShareResourceType resourceType,
        CancellationToken ct
    ) =>
        await db
            .ShareLinks.IgnoreQueryFilters()
            .AsNoTracking()
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
            .ShareLinks.IgnoreQueryFilters()
            .AsNoTracking()
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
            .ShareLinks.IgnoreQueryFilters()
            .AsNoTracking()
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
            .ShareLinks.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(link =>
                link.TenantId == tenantId
                && link.Status == ShareStatus.Active
                && link.ResourceType == ShareResourceType.Folder
                && link.Visibility == ShareVisibility.Public
                && folderIds.Contains(link.ResourceId)
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> ListResourceIdsWithActiveShareAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> resourceIds,
        DateTime nowUtc,
        CancellationToken ct
    ) =>
        await db
            .ShareLinks.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(link =>
                link.TenantId == tenantId
                && resourceIds.Contains(link.ResourceId)
                && link.Status == ShareStatus.Active
                && link.ExpiresAtUtc > nowUtc
                && (link.MaxAccessCount == null || link.AccessCount < link.MaxAccessCount)
            )
            .Select(link => link.ResourceId)
            .Distinct()
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
            .AccessLogs.IgnoreQueryFilters()
            .AsNoTracking()
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
        db
            .DmcaNotices.IgnoreQueryFilters()
            .SingleOrDefaultAsync(notice => notice.TenantId == tenantId && notice.Id == id, ct);

    public Task<bool> HasActiveNoticeForFileAsync(Guid tenantId, Guid fileId, CancellationToken ct) =>
        db
            .DmcaNotices.IgnoreQueryFilters()
            .AnyAsync(
                notice =>
                    notice.TenantId == tenantId && notice.FileId == fileId && ActiveStatuses.Contains(notice.Status),
                ct
            );
}
