using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>
/// Fase C2 — subcarpetas + archivos directamente dentro de folderId (null = raiz del owner
/// visible para el scope). OwnerType/OwnerId (2026-07-20) son un filtro opcional adicional
/// solo relevante para staff interno navegando la raiz de un tenant con muchos duenos
/// mezclados (Tenant + N Customers) — cierra el gap de "dame solo el arbol de este
/// cliente". El portal de cliente ya estaba y sigue estando acotado por
/// Scope.IsCustomerPortal/CustomerId, que gana siempre sobre estos dos filtros.
/// </summary>
public sealed record GetFolderContentsQuery(
    Guid TenantId,
    StorageActorScope Scope,
    Guid? FolderId,
    OwnerType? OwnerType = null,
    Guid? OwnerId = null
);

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

        var subfolders = await folders.ListSubfoldersAsync(
            query.TenantId,
            query.FolderId,
            restrictedCustomerId,
            query.OwnerType,
            query.OwnerId,
            ct
        );
        var filesInFolder = await files.ListInFolderAsync(
            query.TenantId,
            query.FolderId,
            restrictedCustomerId,
            query.OwnerType,
            query.OwnerId,
            ct
        );

        return new FolderContentsResponse(
            subfolders.Select(FolderResponseMapper.Map).ToArray(),
            filesInFolder.Select(FileResponseMapper.Map).ToArray()
        );
    }
}
