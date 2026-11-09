using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.CustomerAccess;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>Fase C2 — mueve un archivo a otra carpeta navegable (o a la raiz con null).</summary>
public sealed record MoveFileToFolderCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid FileId,
    Guid? FolderId,
    RequestAuditContext Audit
);

public static class MoveFileToFolderHandler
{
    // Cota defensiva: ningun arbol real de carpetas navegables llega a esta profundidad.
    private const int MaxAncestorWalkDepth = 64;

    public static async Task<Result> Handle(
        MoveFileToFolderCommand command,
        IFileObjectRepository files,
        IFolderRepository folders,
        IShareLinkRepository shares,
        IStorageAuditRepository audit,
        ISystemClock clock,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(command.TenantId, command.FileId, ct);
        if (file is null || !command.Scope.CanAccess(file))
            return Result.Failure(FileErrors.NotFound);

        // Una foto de perfil nunca entra a una carpeta navegable: ahí aparecería como documento.
        if (UploadRegistration.IsReservedForServices(file.FolderType))
            return Result.Failure(FileErrors.FolderReserved);

        Folder? destination = null;
        if (command.FolderId is { } folderId)
        {
            destination = await folders.GetAsync(command.TenantId, folderId, ct);
            if (destination is null)
                return Result.Failure(FolderErrors.NotFound);
            if (destination.OwnerType != file.OwnerType || destination.OwnerId != file.OwnerId)
                return Result.Failure(FolderErrors.OwnerMismatch);
            // Portal con Upload deny en la carpeta destino: 403 con reason.
            if (command.Scope.IsDeniedForPortal(AccessResourceType.Folder, destination.Id, AccessCapability.Upload))
                return Result.Failure(
                    CustomerAccessDenied.With(
                        command.Scope.DenyReasonFor(AccessResourceType.Folder, destination.Id, AccessCapability.Upload)
                    )
                );
        }

        if (
            file.FolderType == FolderType.ClientForms
            && !string.Equals(destination?.Category, "sys.clientforms", StringComparison.Ordinal)
        )
            return Result.Failure(FileErrors.ExclusiveToClientForms);

        var moved = file.MoveToFolder(command.FolderId, clock.UtcNow);
        if (moved.IsFailure)
            return moved;

        if (command.FolderId is { } destinationFolderId)
            await AlertActivePublicFolderShares(
                command,
                file,
                destinationFolderId,
                folders,
                shares,
                audit,
                bus,
                clock,
                ct
            );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Fase C4 — avisa (audit + evento) si el file recien movido queda dentro del
    /// alcance de un ShareLink Public activo (directo o heredado de un ancestro
    /// recursivo). AutoCovered = AppliesToFutureItems del link: si es true el
    /// acceso ya lo sirve FolderShareCoverage sin nada mas; si es false, es solo
    /// un aviso — el link no lo cubre hasta que alguien lo comparta explicito.
    /// </summary>
    private static async Task AlertActivePublicFolderShares(
        MoveFileToFolderCommand command,
        FileObject file,
        Guid destinationFolderId,
        IFolderRepository folders,
        IShareLinkRepository shares,
        IStorageAuditRepository audit,
        IMessageBus bus,
        ISystemClock clock,
        CancellationToken ct
    )
    {
        var ancestorIds = await CollectSelfAndAncestorIds(command.TenantId, destinationFolderId, folders, ct);
        var candidates = await shares.ListActivePublicFolderSharesAsync(command.TenantId, ancestorIds, ct);

        foreach (var link in candidates)
        {
            var isDirect = link.ResourceId == destinationFolderId;
            if (!isDirect && !link.IsRecursive)
                continue; // este link no alcanza esta carpeta

            var autoCovered = link.AppliesToFutureItems;
            audit.Add(
                StorageAccessLog.Create(
                    command.TenantId,
                    file.Id,
                    command.ActorId,
                    "share.folder-item-added",
                    "alert",
                    command.Audit.IpAddress,
                    command.Audit.UserAgent,
                    command.Audit.CorrelationId,
                    $"shareLinkId={link.Id};autoCovered={autoCovered}",
                    clock.UtcNow
                )
            );
            await bus.PublishAsync(
                new ShareLinkFolderItemAddedIntegrationEvent
                {
                    TenantId = command.TenantId,
                    ShareLinkId = link.Id,
                    FolderId = link.ResourceId,
                    FileId = file.Id,
                    AutoCovered = autoCovered,
                    CreatedByUserId = file.CreatedBy,
                    CorrelationId = command.Audit.CorrelationId,
                }
            );
        }
    }

    private static async Task<IReadOnlyList<Guid>> CollectSelfAndAncestorIds(
        Guid tenantId,
        Guid folderId,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        var ids = new List<Guid>();
        Guid? currentId = folderId;
        for (var depth = 0; depth < MaxAncestorWalkDepth && currentId is { } id; depth++)
        {
            ids.Add(id);
            var folder = await folders.GetAsync(tenantId, id, ct);
            currentId = folder?.ParentFolderId;
        }
        return ids;
    }
}
