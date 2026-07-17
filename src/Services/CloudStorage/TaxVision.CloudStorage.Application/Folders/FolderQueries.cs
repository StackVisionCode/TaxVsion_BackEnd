using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>Fase C2 — subcarpetas + archivos directamente dentro de folderId (null = raiz del owner visible para el scope).</summary>
public sealed record GetFolderContentsQuery(Guid TenantId, StorageActorScope Scope, Guid? FolderId);

public static class GetFolderContentsHandler
{
    public static async Task<FolderContentsResponse> Handle(
        GetFolderContentsQuery query,
        IFolderRepository folders,
        IFileObjectRepository files,
        CancellationToken ct
    )
    {
        var restrictedCustomerId = query.Scope.IsCustomerPortal ? query.Scope.CustomerId ?? Guid.Empty : (Guid?)null;

        var subfolders = await folders.ListSubfoldersAsync(query.TenantId, query.FolderId, restrictedCustomerId, ct);
        var filesInFolder = await files.ListInFolderAsync(query.TenantId, query.FolderId, restrictedCustomerId, ct);

        return new FolderContentsResponse(
            subfolders.Select(FolderResponseMapper.Map).ToArray(),
            filesInFolder.Select(FileResponseMapper.Map).ToArray()
        );
    }
}
