using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Folders;

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
        if (SystemFolderCatalog.IsSystemCategory(loaded.Value.Category))
            return Result.Failure<FolderResponse>(FolderErrors.SystemFolderProtected);

        var nameResult = FolderName.Create(command.NewName);
        if (nameResult.IsFailure)
            return Result.Failure<FolderResponse>(nameResult.Error);

        var folder = loaded.Value;
        if (
            await folders.NameExistsUnderParentAsync(
                command.TenantId,
                folder.ParentFolderId,
                nameResult.Value.Value,
                folder.OwnerType,
                folder.OwnerId,
                folder.Id,
                ct
            )
        )
            return Result.Failure<FolderResponse>(FolderErrors.NameAlreadyExists);

        var applied = await ApplyRenameAndCascade(command.TenantId, folder, nameResult.Value, folders, ct);
        if (applied.IsFailure)
            return Result.Failure<FolderResponse>(applied.Error);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FolderResponseMapper.Map(folder));
    }

    private static async Task<Result> ApplyRenameAndCascade(
        Guid tenantId,
        Folder folder,
        FolderName newName,
        IFolderRepository folders,
        CancellationToken ct
    )
    {
        var oldPath = folder.RelativePath;
        var parentPath = ParentPathOf(oldPath);
        var renamed = folder.Rename(newName, parentPath);
        if (renamed.IsFailure)
            return renamed;
        await FolderPathCascader.CascadeAsync(tenantId, oldPath, folder.RelativePath, folders, ct);
        return Result.Success();
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
