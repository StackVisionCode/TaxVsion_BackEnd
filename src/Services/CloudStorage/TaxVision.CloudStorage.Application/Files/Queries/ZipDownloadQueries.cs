using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Queries;

/// <summary>Fase B2 — un archivo ya resuelto y listo para escribirse como entry del ZIP.</summary>
public sealed record ZipDownloadEntry(Guid FileId, string ObjectKey, string EntryName, long SizeBytes);

public sealed record ZipDownloadPlan(IReadOnlyList<ZipDownloadEntry> Entries);

public sealed record PrepareZipDownloadQuery(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    IReadOnlyList<Guid> FileIds,
    IReadOnlyList<Guid> FolderIds,
    RequestAuditContext Audit
);

/// <summary>
/// Fase B2/B2.1 — valida y arma el plan de una descarga ZIP (archivos sueltos +
/// carpetas completas), pero NO escribe los bytes: eso lo hace el controller via
/// IObjectStorage.DownloadAsync directo al Response.Body (streaming), fuera del
/// bus de Wolverine a proposito (un handler no tiene acceso a HttpResponse).
/// Este handler solo decide QUE va en el ZIP y audita la decision, igual que
/// IssueDownloadUrlHandler audita la emision de una URL presignada sin bajar el
/// archivo el mismo.
///
/// Dos semanticas distintas a proposito:
/// - FileIds explicitos: estrictos — un id invalido/inaccesible/no disponible
///   aborta TODO el pedido (el usuario los eligio uno por uno, un descarte
///   silencioso seria confuso).
/// - FolderIds: tolerantes — una carpeta vacia o con archivos aun escaneando no
///   aborta el resto del ZIP, simplemente no aporta esas entries (bajar "la
///   carpeta X" no deberia fallar entero porque un archivo adentro esta en
///   cuarentena).
///
/// Resolucion de carpetas via RelativePath (path materializado en Folder, ver
/// Domain/Folders/Folder.cs) en vez de recursion recorriendo ParentFolderId
/// nivel por nivel: 1 query (ListByPathPrefixAsync) trae el subarbol completo de
/// cada carpeta pedida, y los archivos de TODAS las carpetas resueltas se traen
/// en un unico batch (ListInFoldersAsync) — evita el N+1 de "una query por
/// carpeta" que tendria una recursion naive.
/// </summary>
public static class PrepareZipDownloadHandler
{
    public static async Task<Result<ZipDownloadPlan>> Handle(
        PrepareZipDownloadQuery query,
        IFileObjectRepository files,
        IFolderRepository folders,
        IStorageAuditRepository audit,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var requestedFileIds = query.FileIds.Distinct().ToArray();
        var requestedFolderIds = query.FolderIds.Distinct().ToArray();
        if (requestedFileIds.Length == 0 && requestedFolderIds.Length == 0)
            return Result.Failure<ZipDownloadPlan>(FileErrors.NoFilesRequested);

        var config = options.Value;

        // Chequeos baratos (sin tocar la BD) antes de cualquier I/O — fail fast.
        if (requestedFileIds.Length > config.MaxZipFiles)
            return Result.Failure<ZipDownloadPlan>(FileErrors.TooManyItems);
        if (requestedFolderIds.Length > config.MaxZipFolders)
            return Result.Failure<ZipDownloadPlan>(FileErrors.TooManyFolders);

        // 1) FileIds explicitos — estrictos (ver docblock de la clase).
        var explicitFiles = new List<FileObject>(requestedFileIds.Length);
        foreach (var fileId in requestedFileIds)
        {
            var file = await files.GetAsync(query.TenantId, fileId, ct);
            if (file is null || !query.Scope.CanAccess(file))
                return Result.Failure<ZipDownloadPlan>(FileErrors.NotFound);
            if (file.Status != FileStatus.Available)
                return Result.Failure<ZipDownloadPlan>(FileErrors.NotAvailable);
            explicitFiles.Add(file);
        }
        var explicitFileIds = explicitFiles.Select(file => file.Id).ToHashSet();

        // 2) FolderIds — resuelve cada subarbol pedido a una carpeta de entries
        // dentro del ZIP (nombre de la carpeta raiz pedida = directorio top-level,
        // con sufijo _1/_2 si dos carpetas pedidas comparten Name).
        var folderEntryPrefixes = new Dictionary<Guid, string>();
        var allFolderIds = new List<Guid>();
        var usedRootPrefixes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var folderId in requestedFolderIds)
        {
            var root = await folders.GetAsync(query.TenantId, folderId, ct);
            if (root is null || !query.Scope.CanAccess(root))
                return Result.Failure<ZipDownloadPlan>(FolderErrors.NotFound);

            var rootPrefix = ReservePrefix(usedRootPrefixes, root.Name);
            folderEntryPrefixes[root.Id] = rootPrefix;
            allFolderIds.Add(root.Id);

            var descendants = await folders.ListByPathPrefixAsync(query.TenantId, root.RelativePath, ct);
            foreach (var descendant in descendants)
            {
                var suffix = descendant.RelativePath[(root.RelativePath.Length + 1)..];
                folderEntryPrefixes[descendant.Id] = $"{rootPrefix}/{suffix}";
                allFolderIds.Add(descendant.Id);
            }
        }

        var restrictedCustomerId = query.Scope.IsCustomerPortal ? query.Scope.CustomerId ?? Guid.Empty : (Guid?)null;
        var folderFiles =
            allFolderIds.Count > 0
                ? await files.ListInFoldersAsync(query.TenantId, allFolderIds, restrictedCustomerId, ct)
                : [];

        // Tolerante: descarta silenciosamente lo que no esta Available y lo que
        // ya vino por FileIds explicitos (evita una entry duplicada en el ZIP).
        var resolvedFolderFiles = folderFiles
            .Where(file => file.Status == FileStatus.Available && !explicitFileIds.Contains(file.Id))
            .ToList();

        var combined = new List<(FileObject File, string? FolderPrefix)>(
            explicitFiles.Count + resolvedFolderFiles.Count
        );
        foreach (var file in explicitFiles)
            combined.Add((file, null));
        foreach (var file in resolvedFolderFiles)
            combined.Add((file, folderEntryPrefixes[file.FolderId!.Value]));

        if (combined.Count == 0)
            return Result.Failure<ZipDownloadPlan>(FileErrors.NoFilesResolved);
        if (combined.Count > config.MaxZipFiles)
            return Result.Failure<ZipDownloadPlan>(FileErrors.TooManyItems);

        var totalBytes = combined.Sum(item => item.File.SizeBytes);
        if (totalBytes > config.MaxZipAggregateBytes)
            return Result.Failure<ZipDownloadPlan>(FileErrors.ZipTooLarge);

        var entries = BuildEntries(combined);

        audit.Add(
            StorageAccessLog.Create(
                query.TenantId,
                null,
                query.ActorId,
                "download.zip",
                "success",
                query.Audit.IpAddress,
                query.Audit.UserAgent,
                query.Audit.CorrelationId,
                $"files={combined.Count};folders={requestedFolderIds.Length};bytes={totalBytes}",
                clock.UtcNow
            )
        );
        foreach (var (file, _) in combined)
        {
            await bus.PublishAsync(
                new FileAccessAuditedIntegrationEvent
                {
                    TenantId = query.TenantId,
                    FileId = file.Id,
                    ActorId = query.ActorId,
                    Action = "download.zip",
                    Outcome = "success",
                    IpAddress = query.Audit.IpAddress,
                    CorrelationId = query.Audit.CorrelationId,
                }
            );
        }
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ZipDownloadPlan(entries));
    }

    /// <summary>Desambigua un nombre repetido a este nivel (root de carpetas pedidas, o nombre de archivo) con sufijo _1, _2...</summary>
    private static string ReservePrefix(Dictionary<string, int> used, string name)
    {
        var occurrence = used.TryGetValue(name, out var count) ? count : 0;
        used[name] = occurrence + 1;
        return occurrence == 0 ? name : $"{name}_{occurrence}";
    }

    /// <summary>
    /// Arma el path final de cada entry (folderPrefix/nombre, o solo nombre si es
    /// un FileId suelto) y desambigua colisiones DENTRO del mismo directorio del
    /// ZIP con sufijo _1, _2... antes de la extension, en el orden pedido.
    /// </summary>
    private static List<ZipDownloadEntry> BuildEntries(IReadOnlyList<(FileObject File, string? FolderPrefix)> items)
    {
        var seenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var entries = new List<ZipDownloadEntry>(items.Count);
        foreach (var (file, folderPrefix) in items)
        {
            var name = file.OriginalName;
            var pathKey = string.IsNullOrEmpty(folderPrefix) ? name : $"{folderPrefix}/{name}";
            var occurrence = seenCounts.TryGetValue(pathKey, out var count) ? count : 0;
            seenCounts[pathKey] = occurrence + 1;

            var dedupedName =
                occurrence == 0
                    ? name
                    : $"{Path.GetFileNameWithoutExtension(name)}_{occurrence}{Path.GetExtension(name)}";
            var entryName = string.IsNullOrEmpty(folderPrefix) ? dedupedName : $"{folderPrefix}/{dedupedName}";

            entries.Add(new ZipDownloadEntry(file.Id, file.ObjectKey, entryName, file.SizeBytes));
        }
        return entries;
    }
}
