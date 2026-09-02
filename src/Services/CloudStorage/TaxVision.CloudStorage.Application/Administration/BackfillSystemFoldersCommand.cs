using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Administration;

/// <summary>Fila agregada del reporte: dueno + tipo → carpeta de sistema, sumando entre tenants.</summary>
public sealed record BackfillGroupReport(string OwnerType, string FolderType, string FolderName, int Count);

/// <summary>
/// Resultado del backfill: cuantos tenants y archivos se archivarian/archivaron, desglosado por
/// (OwnerType, FolderType) — el ownerId se colapsa porque en un barrido multi-tenant es ruido.
/// </summary>
public sealed record BackfillSystemFoldersReport(
    bool DryRun,
    int TenantsProcessed,
    int FilesFiled,
    IReadOnlyList<BackfillGroupReport> Groups
);

/// <summary>
/// Fase 3 — backfill idempotente (una sola vez): coloca en su carpeta de sistema los archivos
/// historicos SIN carpeta (FolderId null) de tipos navegables. <paramref name="TenantId"/> null =
/// barrido de TODOS los tenants que lo necesiten (solo PlatformAdmin); con valor = ese tenant.
/// Con <paramref name="DryRun"/> solo reporta la foto sin mutar. Reejecutable sin duplicar: reusa
/// las carpetas por category (get-or-create) y solo toca archivos que aun estan en raiz.
/// </summary>
public sealed record BackfillSystemFoldersCommand(Guid? TenantId, bool DryRun, int BatchSize);

public static class BackfillSystemFoldersHandler
{
    private const int DefaultBatchSize = 200;
    private const int MaxBatchSize = 1000;

    public static async Task<Result<BackfillSystemFoldersReport>> Handle(
        BackfillSystemFoldersCommand command,
        IFileObjectRepository files,
        ISystemFolderProvisioner provisioner,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var navigable = SystemFolderCatalog.NavigableTypes;

        var tenants = command.TenantId is { } single
            ? [single]
            : await files.DistinctTenantsWithUnfiledFilesAsync(navigable, ct);

        // Acumulador agregado por (OwnerType, FolderType), colapsando ownerId y tenant.
        var aggregate = new Dictionary<(OwnerType OwnerType, FolderType FolderType), int>();
        var totalFiled = 0;

        foreach (var tenantId in tenants)
        {
            var filed = command.DryRun
                ? await SummarizeAsync(tenantId, navigable, files, aggregate, ct)
                : await ApplyAsync(command, tenantId, navigable, files, provisioner, clock, unitOfWork, aggregate, ct);
            totalFiled += filed;
        }

        var groups = aggregate
            .Select(kv => new BackfillGroupReport(
                kv.Key.OwnerType.ToString(),
                kv.Key.FolderType.ToString(),
                SystemFolderCatalog.TryGet(kv.Key.FolderType, out var spec) ? spec.Name : kv.Key.FolderType.ToString(),
                kv.Value
            ))
            .OrderByDescending(g => g.Count)
            .ToList();

        return Result.Success(new BackfillSystemFoldersReport(command.DryRun, tenants.Count, totalFiled, groups));
    }

    private static async Task<int> SummarizeAsync(
        Guid tenantId,
        IReadOnlyCollection<FolderType> navigable,
        IFileObjectRepository files,
        Dictionary<(OwnerType, FolderType), int> aggregate,
        CancellationToken ct
    )
    {
        var groups = await files.SummarizeUnfiledFilesAsync(tenantId, navigable, ct);
        var filed = 0;
        foreach (var group in groups)
        {
            aggregate[(group.OwnerType, group.FolderType)] =
                aggregate.GetValueOrDefault((group.OwnerType, group.FolderType)) + group.Count;
            filed += group.Count;
        }
        return filed;
    }

    private static async Task<int> ApplyAsync(
        BackfillSystemFoldersCommand command,
        Guid tenantId,
        IReadOnlyCollection<FolderType> navigable,
        IFileObjectRepository files,
        ISystemFolderProvisioner provisioner,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        Dictionary<(OwnerType, FolderType), int> aggregate,
        CancellationToken ct
    )
    {
        var batchSize = Math.Clamp(command.BatchSize <= 0 ? DefaultBatchSize : command.BatchSize, 1, MaxBatchSize);
        // Caché por (dueño, CATEGORY), no por FolderType: distintos FolderType pueden compartir carpeta
        // (EmailIncoming + EmailOutgoing → "Email"/sys.email). Sin esto, dos archivos de un mismo dueño y
        // category en un mismo batch (aún sin commitear) crean dos carpetas y chocan con IX_Folders_Owner_Category.
        var folderCache = new Dictionary<(OwnerType, Guid?, string), Guid>();
        var totalFiled = 0;

        while (true)
        {
            var batch = await files.NextUnfiledFilesAsync(tenantId, navigable, batchSize, ct);
            if (batch.Count == 0)
                break;

            var filedInBatch = 0;
            foreach (var file in batch)
            {
                var category = SystemFolderCatalog.TryGet(file.FolderType, out var spec)
                    ? spec.Category
                    : file.FolderType.ToString();
                var key = (file.OwnerType, file.OwnerId, category);
                if (!folderCache.TryGetValue(key, out var folderId))
                {
                    var resolved = await provisioner.ResolveFolderIdAsync(
                        tenantId,
                        file.OwnerType,
                        file.OwnerId,
                        file.FolderType,
                        file.CreatedBy,
                        clock.UtcNow,
                        ct
                    );
                    if (resolved is null)
                        continue; // defensivo: el batch ya viene filtrado a tipos navegables
                    folderId = resolved.Value;
                    folderCache[key] = folderId;
                }

                file.MoveToFolder(folderId, clock.UtcNow);
                filedInBatch++;
                totalFiled++;
                aggregate[(file.OwnerType, file.FolderType)] =
                    aggregate.GetValueOrDefault((file.OwnerType, file.FolderType)) + 1;
            }

            await unitOfWork.SaveChangesAsync(ct);

            // Corta si un lote no logro archivar nada (evita bucle infinito ante un caso no previsto).
            if (filedInBatch == 0)
                break;
        }

        return totalFiled;
    }
}
