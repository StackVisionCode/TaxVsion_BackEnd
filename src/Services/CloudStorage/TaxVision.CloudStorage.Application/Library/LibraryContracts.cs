namespace TaxVision.CloudStorage.Application.Library;

/// <summary>Parámetros de búsqueda tal como llegan en la query string.</summary>
public sealed class LibrarySearchRequest
{
    public string? Q { get; set; }
    public string[]? Source { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? UploadedBy { get; set; }
    public string[]? TypeGroup { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int? TaxYear { get; set; }
    public Guid? FileId { get; set; }
    public string? Sort { get; set; }
    public int? Take { get; set; }
    public string? Cursor { get; set; }
    public bool? Counts { get; set; }

    // Solo la búsqueda dentro de Documents.
    public string? OwnerType { get; set; }
    public Guid? OwnerId { get; set; }
}

/// <param name="Context">Claves extra de la ruta del CRM, o null.</param>
public sealed record LibraryOpenIn(
    string ResourceType,
    Guid ResourceId,
    Guid? CustomerId,
    IReadOnlyDictionary<string, string>? Context
);

/// <summary>Con qué se abre la vista previa de una fila.</summary>
public static class LibraryPreviews
{
    public const string Viewer = "viewer";
    public const string Recording = "recording";
}

/// <param name="Preview"><see cref="LibraryPreviews"/>.</param>
/// <param name="RestrictedTo">Marca de privacidad del dueño, tal cual; null en los orígenes SQL.</param>
public sealed record LibraryItemResponse(
    Guid Id,
    string Name,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    int? TaxYear,
    string Source,
    string Kind,
    Guid? CustomerId,
    string? FolderPath,
    string? ResourceTitle,
    LibraryOpenIn OpenIn,
    bool CanPreview,
    bool CanDownload,
    string Preview,
    string? RestrictedTo
);

/// <param name="Count">Null en los orígenes por fila.</param>
public sealed record LibrarySourceCount(string Source, int? Count);

/// <param name="Total">Null si algún origen no tiene número.</param>
public sealed record LibraryCounts(int? Total, IReadOnlyList<LibrarySourceCount> BySource);

/// <param name="NextCursor">Null = no hay más; es la única señal de fin.</param>
/// <param name="UnavailableSources">Orígenes por fila cuyo dueño no respondió.</param>
/// <param name="PartialSources">Orígenes cuyo ámbito se cortó por el tope: solo salen los de actividad más reciente.</param>
public sealed record LibrarySearchResponse(
    IReadOnlyList<LibraryItemResponse> Items,
    string? NextCursor,
    LibraryCounts? Counts,
    IReadOnlyList<string> UnavailableSources,
    IReadOnlyList<string> PartialSources
);

public sealed record LibrarySourceCapability(string Id, bool RowScoped, bool CanPreview, bool CanDownload);

public sealed record LibrarySourcesResponse(IReadOnlyList<LibrarySourceCapability> Sources, bool AssignmentRestricted);
