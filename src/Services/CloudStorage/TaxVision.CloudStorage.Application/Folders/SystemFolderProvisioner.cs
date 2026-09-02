using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>
/// Resuelve (get-or-create) la carpeta de sistema donde debe aterrizar un archivo
/// guardado por un servicio (M2M) segun su <see cref="FolderType"/>. Devuelve null
/// cuando el tipo es interno (no navegable). No persiste: agrega la carpeta nueva al
/// repositorio y deja el SaveChanges al handler llamante (misma unidad de trabajo que
/// el archivo). La idempotencia ante carreras la respalda el indice unico
/// IX_Folders_Owner_Category: si dos guardados concurrentes crean la misma carpeta,
/// uno choca y su mensaje se reintenta, encontrando ya la carpeta del ganador.
/// </summary>
public interface ISystemFolderProvisioner
{
    Task<Guid?> ResolveFolderIdAsync(
        Guid tenantId,
        OwnerType ownerType,
        Guid? ownerId,
        FolderType folderType,
        Guid actorId,
        DateTime nowUtc,
        CancellationToken ct
    );
}

public sealed class SystemFolderProvisioner(IFolderRepository folders) : ISystemFolderProvisioner
{
    public async Task<Guid?> ResolveFolderIdAsync(
        Guid tenantId,
        OwnerType ownerType,
        Guid? ownerId,
        FolderType folderType,
        Guid actorId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        // Tipo interno (Branding/Templates/Recordings/...): no navegable, el archivo se queda en raiz.
        if (!SystemFolderCatalog.TryGet(folderType, out var spec))
            return null;

        var existing = await folders.GetByOwnerAndCategoryAsync(tenantId, ownerType, ownerId, spec.Category, ct);
        if (existing is not null)
            return existing.Id;

        var name = FolderName.Create(spec.Name);
        var category = FolderCategory.Create(spec.Category);
        var folder = Folder.Create(
            Guid.NewGuid(),
            tenantId,
            ownerType,
            ownerId,
            parentFolderId: null,
            name.Value,
            parentRelativePath: null,
            actorId,
            nowUtc,
            category.Value
        );
        folders.Add(folder.Value);
        return folder.Value.Id;
    }
}
