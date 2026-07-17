using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files.RecycleBin;

/// <summary>Fase C1 — item de la papelera, con los timestamps que la vista de recuperacion necesita mostrar.</summary>
public sealed record RecycleBinItemResponse(
    Guid Id,
    OwnerType OwnerType,
    Guid? OwnerId,
    FolderType FolderType,
    string OriginalName,
    long SizeBytes,
    DateTime SoftDeletedAtUtc,
    DateTime SoftDeleteExpiresAtUtc
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
}
