using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files.Commands;

/// <summary>
/// Consumer de <see cref="ReassignFileOwnerRequestedIntegrationEvent"/>: re-asigna el dueno
/// logico de un archivo ya catalogado (sin tocar MinIO) y lo re-archiva en la carpeta de sistema
/// del nuevo dueno. Lo usa la migracion de documentos firmados (Signature) para que los sellados
/// existentes (OwnerType=Signature) pasen a pertenecer al cliente y aparezcan bajo su carpeta en
/// Documents. Idempotente: reejecutar deja el archivo igual (mismo dueno + misma carpeta por category).
/// </summary>
public static class ReassignFileOwnerHandler
{
    public static async Task Handle(
        ReassignFileOwnerRequestedIntegrationEvent evt,
        IFileObjectRepository files,
        ISystemFolderProvisioner systemFolders,
        IStorageAuditRepository audit,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<ReassignFileOwnerRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            var file = await files.GetAsync(evt.TenantId, evt.FileId, ct);
            if (file is null)
            {
                // Signature conoce el FileId desde el sellado: si es reciente, SaveFileRequested puede seguir en vuelo.
                // Agotados los reintentos va a la cola de errores, visible.
                if (clock.UtcNow - evt.OccurredOn < TimeSpan.FromMinutes(options.Value.ReassignOwnerFileWaitMinutes))
                    throw new DependencyNotReadyException("FileObject", $"file {evt.FileId} is not cataloged yet");

                // Pasado el plazo ya no se espera: purgado o id ajeno. Queda en el log y en la auditoría.
                logger.LogWarning("ReassignFileOwner {FileId}: file not found, skipping.", evt.FileId);
                audit.Add(
                    StorageAccessLog.Create(
                        evt.TenantId,
                        evt.FileId,
                        evt.ActorId,
                        "owner.reassign-skipped",
                        "skipped",
                        null,
                        null,
                        correlation.CorrelationId,
                        $"reason=file_not_found;newOwnerType={evt.NewOwnerType}",
                        clock.UtcNow
                    )
                );
                await unitOfWork.SaveChangesAsync(ct);
                return;
            }

            if (file.FolderType == FolderType.ClientForms)
            {
                logger.LogWarning("ReassignFileOwner {FileId}: Client Forms files are exclusive.", evt.FileId);
                return;
            }

            if (!Enum.TryParse<OwnerType>(evt.NewOwnerType, ignoreCase: true, out var ownerType))
            {
                logger.LogError(
                    "ReassignFileOwner {FileId}: invalid OwnerType '{OwnerType}' — dropping.",
                    evt.FileId,
                    evt.NewOwnerType
                );
                return;
            }

            // Un participante externo solo recibe archivos por el flujo propio de Client Forms.
            if (ownerType == OwnerType.FormParticipant)
            {
                logger.LogWarning(
                    "ReassignFileOwner {FileId}: FormParticipant is not a valid destination owner — dropping.",
                    evt.FileId
                );
                return;
            }

            var reassigned = file.ReassignOwner(ownerType, evt.NewOwnerId, clock.UtcNow);
            if (reassigned.IsFailure)
            {
                logger.LogWarning("ReassignFileOwner {FileId}: {Error}.", evt.FileId, reassigned.Error.Code);
                return;
            }

            if (options.Value.AutoSystemFolders)
            {
                var folderId = await systemFolders.ResolveFolderIdAsync(
                    evt.TenantId,
                    ownerType,
                    evt.NewOwnerId,
                    file.FolderType,
                    evt.ActorId,
                    clock.UtcNow,
                    ct
                );
                if (folderId is { } fid)
                    file.MoveToFolder(fid, clock.UtcNow);
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
