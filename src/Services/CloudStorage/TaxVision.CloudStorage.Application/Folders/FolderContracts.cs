using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>Clave de orden del contenido de carpeta (espejo del selector del front).</summary>
public enum FileSortKey
{
    Name,
    Modified,
    Size,
}

/// <summary>
/// Filtro + orden server-side del contenido de carpeta. FolderTypes acota los tipos visibles del
/// explorador (el front manda el set "user-facing"); TaxYears/Extensions/Statuses son los chips.
/// <see cref="HasFileFilters"/> = hay un filtro que solo aplica a archivos → las carpetas se ocultan
/// (una carpeta no puede "ser PDF de 2024"). FolderTypes NO cuenta como file-filter (solo acota tipos).
/// </summary>
public sealed record FolderContentsFilter(
    IReadOnlyList<FolderType>? FolderTypes = null,
    IReadOnlyList<int>? TaxYears = null,
    IReadOnlyList<string>? Extensions = null,
    IReadOnlyList<FileStatus>? Statuses = null,
    FileSortKey SortKey = FileSortKey.Name,
    bool SortDescending = false
)
{
    public bool HasFileFilters =>
        TaxYears is { Count: > 0 } || Extensions is { Count: > 0 } || Statuses is { Count: > 0 };

    public static readonly FolderContentsFilter None = new();
}

public sealed record FolderResponse(
    Guid Id,
    OwnerType OwnerType,
    Guid? OwnerId,
    Guid? ParentFolderId,
    string Name,
    string RelativePath,
    string? Category,
    DateTime CreatedAtUtc,
    // true si la carpeta tiene un link de compartir vigente (indicador del listado). Lo setea solo el
    // listado de carpeta; el resto de mapeos lo dejan en false.
    bool IsShared = false
);

/// <summary>
/// Contenido de una carpeta. Subfolders/Files son la PÁGINA actual (carpetas primero); los contadores
/// son del total sin paginar para que el front arme los controles. Sin paginar (Take null) la página
/// es todo el contenido.
/// </summary>
public sealed record FolderContentsResponse(
    IReadOnlyList<FolderResponse> Subfolders,
    IReadOnlyList<FileResponse> Files,
    int FolderCount,
    int FileCount,
    int TotalCount,
    int Skip,
    int? Take
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
