using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>Fase C2 — re-padrea una carpeta (o la sube a raiz con null) y cascadea el nuevo path a todo su subarbol.</summary>
public sealed record MoveFolderCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid FolderId,
    Guid? NewParentFolderId
);

public static class MoveFolderHandler
{
    // Cota defensiva, igual que MoveFileToFolderHandler: un ciclo ya existente no cuelga el movimiento.
    private const int MaxAncestorWalkDepth = 64;

    public static async Task<Result<FolderResponse>> Handle(
        MoveFolderCommand command,
        IFolderRepository folders,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await LockMoveAsync(command, folders, ct);

        var loaded = await RenameFolderHandler.LoadOwnedFolder(
            command.TenantId,
            command.Scope,
            command.FolderId,
            folders,
            ct
        );
        if (loaded.IsFailure)
            return Result.Failure<FolderResponse>(loaded.Error);
        if (SystemFolderCatalog.IsSystemCategory(loaded.Value.Category))
            return Result.Failure<FolderResponse>(FolderErrors.SystemFolderProtected);

        var folder = loaded.Value;
        var newParentResult = await ResolveNewParent(command, folder, folders, ct);
        if (newParentResult.IsFailure)
            return Result.Failure<FolderResponse>(newParentResult.Error);

        if (
            await folders.NameExistsUnderParentAsync(
                command.TenantId,
                command.NewParentFolderId,
                folder.Name,
                folder.OwnerType,
                folder.OwnerId,
                folder.Id,
                ct
            )
        )
            return Result.Failure<FolderResponse>(FolderErrors.NameAlreadyExists);

        var applied = await ApplyMoveAndCascade(
            command.TenantId,
            folder,
            command.NewParentFolderId,
            newParentResult.Value,
            folders,
            ct
        );
        if (applied.IsFailure)
            return Result.Failure<FolderResponse>(applied.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FolderResponseMapper.Map(folder));
    }

    /// <summary>
    /// Candado sobre la carpeta, el destino y los ancestros del destino antes del chequeo de ciclo: dos
    /// movimientos que se cruzan por sus descendientes no comparten otras filas que esas.
    /// </summary>
    private static async Task LockMoveAsync(MoveFolderCommand command, IFolderRepository folders, CancellationToken ct)
    {
        // Orden fijo por Id: el cruce directo hace fila en vez de bloquearse. Si aun así hay
        // interbloqueo por los ancestros, SQL elige víctima y sale como conflicto.
        var locked = new[] { command.FolderId, command.NewParentFolderId ?? command.FolderId }.Distinct().Order();
        Folder? ancestor = null;
        foreach (var id in locked)
        {
            var row = await folders.GetForUpdateAsync(command.TenantId, id, ct);
            if (id == command.NewParentFolderId)
                ancestor = row;
        }

        for (var depth = 0; ancestor?.ParentFolderId is { } next && depth < MaxAncestorWalkDepth; depth++)
            ancestor = await folders.GetForUpdateAsync(command.TenantId, next, ct);
    }

    private static async Task<Result<string?>> ResolveNewParent(
        MoveFolderCommand command,
        Folder folder,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        if (command.NewParentFolderId is not { } newParentId)
            return Result.Success<string?>(null);

        if (newParentId == folder.Id)
            return Result.Failure<string?>(FolderErrors.CircularReference);

        var newParent = await folders.GetAsync(command.TenantId, newParentId, ct);
        if (newParent is null)
            return Result.Failure<string?>(FolderErrors.ParentNotFound);
        if (newParent.OwnerType != folder.OwnerType || newParent.OwnerId != folder.OwnerId)
            return Result.Failure<string?>(FolderErrors.OwnerMismatch);

        // El nuevo padre no puede ser un descendiente de la carpeta que se mueve —
        // eso crearia un ciclo. Con path materializado alcanza con el prefijo.
        var ownPrefix = folder.RelativePath + "/";
        if (
            newParent.RelativePath == folder.RelativePath
            || newParent.RelativePath.StartsWith(ownPrefix, StringComparison.Ordinal)
        )
            return Result.Failure<string?>(FolderErrors.CircularReference);

        return Result.Success<string?>(newParent.RelativePath);
    }

    private static async Task<Result> ApplyMoveAndCascade(
        Guid tenantId,
        Folder folder,
        Guid? newParentFolderId,
        string? newParentPath,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        var oldPath = folder.RelativePath;
        var moved = folder.Reparent(newParentFolderId, newParentPath);
        if (moved.IsFailure)
            return moved;
        await FolderPathCascader.CascadeAsync(tenantId, oldPath, folder.RelativePath, folders, ct);
        return Result.Success();
    }
}
