using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Folders;

public sealed record FolderResponse(
    Guid Id,
    OwnerType OwnerType,
    Guid? OwnerId,
    Guid? ParentFolderId,
    string Name,
    string RelativePath,
    string? Category,
    DateTime CreatedAtUtc
);

public sealed record FolderContentsResponse(
    IReadOnlyList<FolderResponse> Subfolders,
    IReadOnlyList<FileResponse> Files
);

internal static class FolderResponseMapper
{
    public static FolderResponse Map(Folder folder) =>
        new(
            folder.Id,
            folder.OwnerType,
            folder.OwnerId,
            folder.ParentFolderId,
            folder.Name,
            folder.RelativePath,
            folder.Category,
            folder.CreatedAtUtc
        );
}
