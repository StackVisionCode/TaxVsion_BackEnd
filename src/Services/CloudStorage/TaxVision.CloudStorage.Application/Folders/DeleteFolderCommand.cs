using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Platform;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>
/// Borra una carpeta navegable de forma RECURSIVA (UX tipo Drive): manda todos sus archivos —los
/// directos y los de cualquier subcarpeta— a la papelera (soft-delete, recuperables durante la
/// retención) y elimina el subárbol de carpetas. No hace falta vaciarla antes. Guardas: no toca
/// carpetas de sistema y falla en bloque (sin borrado parcial) si algún archivo está en retención
/// legal. No borra MinIO: los bytes viven hasta que el purger los limpie tras la retención.
/// </summary>
public sealed record DeleteFolderCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid FolderId,
    RequestAuditContext Audit,
    // A1 — si el caller tiene cloudstorage.file.delete. Borrar una carpeta manda TODOS sus archivos a la
    // papelera, así que con folder.manage a secas se podía borrar el contenido de la oficina entera sin
    // tener el permiso de borrar archivos. El default true conserva el comportamiento anterior para
    // cualquier llamador que todavía no lo informe.
    bool CanDeleteFiles = true
);

public static class DeleteFolderHandler
{
    public static async Task<Result> Handle(
        DeleteFolderCommand command,
        IFolderRepository folders,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        IStoragePlatformSettingsProvider platform,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var loaded = await RenameFolderHandler.LoadOwnedFolder(
            command.TenantId,
            command.Scope,
            command.FolderId,
            folders,
            ct
        );
        if (loaded.IsFailure)
            return Result.Failure(loaded.Error);
        var root = loaded.Value;
        if (SystemFolderCatalog.IsSystemCategory(root.Category))
            return Result.Failure(FolderErrors.SystemFolderProtected);

        // Subárbol completo: la carpeta + todos sus descendientes (por prefijo de RelativePath).
        var descendants = await folders.ListByPathPrefixAsync(command.TenantId, root.RelativePath, ct);
        // Defensa en profundidad: ninguna carpeta de sistema debe caer dentro de una de usuario.
        if (descendants.Any(f => SystemFolderCatalog.IsSystemCategory(f.Category)))
            return Result.Failure(FolderErrors.SystemFolderProtected);

        var allFolders = descendants.Prepend(root).ToList();
        var allFolderIds = allFolders.Select(f => f.Id).ToList();

        // Archivos activos del subárbol (tracked para mandarlos a la papelera).
        var filesInside = await files.ListInFoldersForUpdateAsync(command.TenantId, allFolderIds, ct);

        // Fail-closed: no se borra una carpeta con archivos en retención legal (sin borrado parcial).
        if (filesInside.Any(f => f.IsLegalHeld))
            return Result.Failure(FolderErrors.HasLegalHold);

        // A1 — borrar la carpeta borra su contenido: eso exige el permiso de borrar archivos, no solo el
        // de administrar carpetas. Una carpeta VACÍA se sigue borrando con folder.manage a secas: no hay
        // nada de nadie que se pierda.
        if (filesInside.Count > 0 && !command.CanDeleteFiles)
            return Result.Failure(FolderErrors.FileDeletePermissionRequired);

        var now = clock.UtcNow;
        var retention = TimeSpan.FromDays((await platform.GetAsync(ct)).RecycleBinRetentionDays);
        // El batch agrupa toda la operación; la carpeta RAÍZ usa su propio Id como batchId (así la
        // papelera lista una sola entrada por carpeta borrada y restaura/purga todo el subárbol junto).
        var batchId = root.Id;

        foreach (var file in filesInside)
        {
            // Se conserva el FolderId: al restaurar la carpeta, el archivo vuelve a su sitio original.
            var soft = file.SoftDelete(now, retention, batchId);
            if (soft.IsFailure)
                return soft;
            audit.Add(
                StorageAccessLog.Create(
                    command.TenantId,
                    file.Id,
                    command.ActorId,
                    "delete.soft",
                    "success",
                    command.Audit.IpAddress,
                    command.Audit.UserAgent,
                    command.Audit.CorrelationId,
                    $"via=folder-delete;batch={batchId}",
                    now
                )
            );
        }

        // La carpeta entera (y su subárbol) va a la papelera — recuperable, no se borra en duro.
        foreach (var folder in allFolders)
        {
            var soft = folder.SoftDelete(batchId, now, retention);
            if (soft.IsFailure)
                return soft;
        }

        audit.Add(
            StorageAccessLog.Create(
                command.TenantId,
                root.Id,
                command.ActorId,
                "folder.delete",
                "success",
                command.Audit.IpAddress,
                command.Audit.UserAgent,
                command.Audit.CorrelationId,
                $"folders={allFolders.Count};files={filesInside.Count};batch={batchId}",
                now
            )
        );

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var file in filesInside)
            await bus.PublishAsync(
                new FileDeletedIntegrationEvent
                {
                    TenantId = command.TenantId,
                    FileId = file.Id,
                    CreatedBy = file.CreatedBy,
                    CorrelationId = command.Audit.CorrelationId,
                }
            );
        return Result.Success();
    }
}
