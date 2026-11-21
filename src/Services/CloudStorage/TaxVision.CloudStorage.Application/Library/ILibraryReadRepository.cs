using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Library;

/// <summary>Fila de <c>Files</c> que la biblioteca necesita; el tipo de contenido ya resuelto (detectado o declarado).</summary>
public sealed record LibraryRow(
    Guid Id,
    string Name,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc,
    Guid CreatedBy,
    int? TaxYear,
    OwnerType OwnerType,
    Guid? OwnerId,
    FolderType FolderType,
    Guid? FolderId,
    string? SourceResourceType,
    Guid? SourceResourceId,
    string? SourceRole,
    Guid? RelatedCustomerId
);

/// <summary>
/// Lecturas de la biblioteca. Todas aplican el mismo predicado: tenant explícito, solo disponibles, lista blanca de
/// orígenes del alcance y recorte por cliente asignado donde toque.
/// </summary>
public interface ILibraryReadRepository
{
    /// <summary>Un tramo ordenado por <see cref="LibraryFilter.Sort"/> a partir de su cursor; ignora el filtro de origen.</summary>
    Task<IReadOnlyList<LibraryRow>> SearchAsync(
        Guid tenantId,
        LibraryReadScope scope,
        LibraryFilter filter,
        int take,
        CancellationToken ct
    );

    Task<LibraryRow?> FindAsync(Guid tenantId, LibraryReadScope scope, Guid fileId, CancellationToken ct);

    /// <summary>Filas por origen con los filtros, sin cursor ni filtro de origen.</summary>
    Task<IReadOnlyDictionary<LibrarySource, int>> CountBySourceAsync(
        Guid tenantId,
        LibraryReadScope scope,
        LibraryFilter filter,
        CancellationToken ct
    );

    Task<IReadOnlyDictionary<Guid, string>> FolderPathsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> folderIds,
        CancellationToken ct
    );
}
