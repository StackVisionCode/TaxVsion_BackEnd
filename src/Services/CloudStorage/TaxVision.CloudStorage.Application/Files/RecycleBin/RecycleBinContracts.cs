using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Files.RecycleBin;

/// <summary>
/// Item de la papelera. <see cref="ItemType"/> distingue archivo suelto de carpeta borrada (que se
/// muestra como UNA entrada y se restaura/purga en bloque). Para carpetas, <see cref="ItemCount"/> es
/// cuántos archivos contiene el batch y <see cref="FolderType"/> no aplica.
/// </summary>
public sealed record RecycleBinItemResponse(
    Guid Id,
    OwnerType OwnerType,
    Guid? OwnerId,
    FolderType FolderType,
    string OriginalName,
    long SizeBytes,
    DateTime SoftDeletedAtUtc,
    DateTime SoftDeleteExpiresAtUtc,
    string ItemType = "File",
    int ItemCount = 0
);

internal static class RecycleBinItemMapper
{
    public static RecycleBinItemResponse Map(FileObject file) =>
        new(
            file.Id,
            file.OwnerType,
            file.OwnerId,
            file.FolderType,
            file.OriginalName,
            file.SizeBytes,
            file.SoftDeletedAtUtc!.Value,
            file.SoftDeleteExpiresAtUtc!.Value
        );

    public static RecycleBinItemResponse MapFolder(Folder folder, int fileCount) =>
        new(
            folder.Id,
            folder.OwnerType,
            folder.OwnerId,
            FolderType.Documents, // no aplica a carpetas; la UI usa ItemType para el icono
            folder.Name,
            0,
            folder.SoftDeletedAtUtc!.Value,
            folder.SoftDeleteExpiresAtUtc!.Value,
            "Folder",
            fileCount
        );
}
