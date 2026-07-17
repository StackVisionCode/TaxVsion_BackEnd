using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>Fase C2 — crea una carpeta navegable (arbol logico, no toca MinIO).</summary>
public sealed record CreateFolderCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid? ParentFolderId,
    string? Name,
    OwnerType OwnerType,
    Guid? OwnerId
);

public static class CreateFolderHandler
{
    public static async Task<Result<FolderResponse>> Handle(
        CreateFolderCommand command,
        IFolderRepository folders,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (!command.Scope.CanCreate(command.OwnerType, command.OwnerId))
            return Result.Failure<FolderResponse>(FolderErrors.Forbidden);

        var nameResult = FolderName.Create(command.Name);
        if (nameResult.IsFailure)
            return Result.Failure<FolderResponse>(nameResult.Error);

        var parentPathResult = await ResolveParentPath(command, folders, ct);
        if (parentPathResult.IsFailure)
            return Result.Failure<FolderResponse>(parentPathResult.Error);

        if (
            await folders.NameExistsUnderParentAsync(
                command.TenantId,
                command.ParentFolderId,
                nameResult.Value.Value,
                null,
                ct
            )
        )
            return Result.Failure<FolderResponse>(FolderErrors.NameAlreadyExists);

        var created = Folder.Create(
            Guid.NewGuid(),
            command.TenantId,
            command.OwnerType,
            command.OwnerId,
            command.ParentFolderId,
            nameResult.Value,
            parentPathResult.Value,
            command.ActorId,
            clock.UtcNow
        );
        if (created.IsFailure)
            return Result.Failure<FolderResponse>(created.Error);

        folders.Add(created.Value);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FolderResponseMapper.Map(created.Value));
    }

    private static async Task<Result<string?>> ResolveParentPath(
        CreateFolderCommand command,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        if (command.ParentFolderId is not { } parentId)
            return Result.Success<string?>(null);

        var parent = await folders.GetAsync(command.TenantId, parentId, ct);
        if (parent is null)
            return Result.Failure<string?>(FolderErrors.ParentNotFound);
        if (parent.OwnerType != command.OwnerType || parent.OwnerId != command.OwnerId)
            return Result.Failure<string?>(FolderErrors.OwnerMismatch);

        return Result.Success<string?>(parent.RelativePath);
    }
}

/// <summary>Fase C2 — renombra una carpeta y cascadea el nuevo path a todo su subarbol.</summary>
public sealed record RenameFolderCommand(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid FolderId,
    string? NewName
);

public static class RenameFolderHandler
{
    public static async Task<Result<FolderResponse>> Handle(
        RenameFolderCommand command,
        IFolderRepository folders,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var loaded = await LoadOwnedFolder(command.TenantId, command.Scope, command.FolderId, folders, ct);
        if (loaded.IsFailure)
            return Result.Failure<FolderResponse>(loaded.Error);

        var nameResult = FolderName.Create(command.NewName);
        if (nameResult.IsFailure)
            return Result.Failure<FolderResponse>(nameResult.Error);

        var folder = loaded.Value;
        if (
            await folders.NameExistsUnderParentAsync(
                command.TenantId,
                folder.ParentFolderId,
                nameResult.Value.Value,
                folder.Id,
                ct
            )
        )
            return Result.Failure<FolderResponse>(FolderErrors.NameAlreadyExists);

        await ApplyRenameAndCascade(command.TenantId, folder, nameResult.Value, folders, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FolderResponseMapper.Map(folder));
    }

    private static async Task ApplyRenameAndCascade(
        Guid tenantId,
        Folder folder,
        FolderName newName,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        var oldPath = folder.RelativePath;
        var parentPath = ParentPathOf(oldPath);
        folder.Rename(newName, parentPath);
        await FolderPathCascader.CascadeAsync(tenantId, oldPath, folder.RelativePath, folders, ct);
    }

    internal static async Task<Result<Folder>> LoadOwnedFolder(
        Guid tenantId,
        StorageActorScope scope,
        Guid folderId,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        var folder = await folders.GetAsync(tenantId, folderId, ct);
        if (folder is null || !scope.CanAccess(folder))
            return Result.Failure<Folder>(FolderErrors.NotFound);
        return Result.Success(folder);
    }

    private static string? ParentPathOf(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return lastSlash <= 0 ? null : path[..lastSlash];
    }
}

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
    public static async Task<Result<FolderResponse>> Handle(
        MoveFolderCommand command,
        IFolderRepository folders,
        IUnitOfWork unitOfWork,
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
            return Result.Failure<FolderResponse>(loaded.Error);

        var folder = loaded.Value;
        var newParentResult = await ResolveNewParent(command, folder, folders, ct);
        if (newParentResult.IsFailure)
            return Result.Failure<FolderResponse>(newParentResult.Error);

        if (
            await folders.NameExistsUnderParentAsync(
                command.TenantId,
                command.NewParentFolderId,
                folder.Name,
                folder.Id,
                ct
            )
        )
            return Result.Failure<FolderResponse>(FolderErrors.NameAlreadyExists);

        await ApplyMoveAndCascade(
            command.TenantId,
            folder,
            command.NewParentFolderId,
            newParentResult.Value,
            folders,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FolderResponseMapper.Map(folder));
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

    private static async Task ApplyMoveAndCascade(
        Guid tenantId,
        Folder folder,
        Guid? newParentFolderId,
        string? newParentPath,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        var oldPath = folder.RelativePath;
        folder.Reparent(newParentFolderId, newParentPath);
        await FolderPathCascader.CascadeAsync(tenantId, oldPath, folder.RelativePath, folders, ct);
    }
}

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

        if (command.FolderId is { } folderId)
        {
            var folder = await folders.GetAsync(command.TenantId, folderId, ct);
            if (folder is null)
                return Result.Failure(FolderErrors.NotFound);
            if (folder.OwnerType != file.OwnerType || folder.OwnerId != file.OwnerId)
                return Result.Failure(FolderErrors.OwnerMismatch);
        }

        file.MoveToFolder(command.FolderId, clock.UtcNow);

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
