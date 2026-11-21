using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Library;

public enum LibrarySort
{
    Newest,
    Oldest,
    Name,
}

public enum LibraryTypeGroup
{
    Pdf,
    Image,
    Spreadsheet,
    Document,
    Media,
    Other,
}

/// <summary>Filtros de la biblioteca ya validados.</summary>
public sealed record LibraryFilter
{
    public const int DefaultTake = 50;
    public const int MaxTake = 100;
    public const int MinSearchLength = 2;
    public const int MaxSearchLength = 100;
    public const int MinTaxYear = 1990;
    public const int MaxTaxYear = 2100;

    /// <summary>Texto recortado; null si es más corto que <see cref="MinSearchLength"/>.</summary>
    public string? Search { get; private init; }
    public IReadOnlyList<LibrarySource> Sources { get; private init; } = [];
    public Guid? CustomerId { get; private init; }
    public Guid? UploadedBy { get; private init; }
    public IReadOnlyList<LibraryTypeGroup> TypeGroups { get; private init; } = [];
    public DateTime? FromUtc { get; private init; }
    public DateTime? ToUtc { get; private init; }
    public int? TaxYear { get; private init; }
    public Guid? FileId { get; private init; }
    public LibrarySort Sort { get; private init; }
    public int Take { get; private init; } = DefaultTake;
    public LibraryCursor? Cursor { get; private init; }
    public bool IncludeCounts { get; private init; }
    public OwnerType? OwnerType { get; private init; }
    public Guid? OwnerId { get; private init; }

    /// <summary>Los mismos filtros a partir de la última fila examinada.</summary>
    public LibraryFilter Continue(LibraryCursor? cursor) => cursor is null ? this : this with { Cursor = cursor };

    /// <summary>Patrón "contiene" para LIKE con <c>\</c> como escape.</summary>
    public string? SearchPattern => Search is null ? null : $"%{EscapeLike(Search)}%";

    /// <param name="forcedSource">Búsqueda dentro de un origen (Documents): fija el origen y acepta el dueño.</param>
    public static Result<LibraryFilter> Create(LibrarySearchRequest request, LibrarySource? forcedSource = null)
    {
        var search = request.Q?.Trim();
        if (search is { Length: > MaxSearchLength })
            return Invalid($"q must have at most {MaxSearchLength} characters.");
        if (request.Take is { } take && (take < 1 || take > MaxTake))
            return Invalid($"take must be between 1 and {MaxTake}.");
        if (request.TaxYear is { } year && (year < MinTaxYear || year > MaxTaxYear))
            return Invalid($"taxYear must be between {MinTaxYear} and {MaxTaxYear}.");
        if (request.From is { } from && request.To is { } to && from >= to)
            return Invalid("from must be earlier than to.");
        if (!TryParseSort(request.Sort, out var sort))
            return Invalid("sort must be newest, oldest or name.");
        if (!TryParseSources(request.Source, forcedSource, out var sources))
            return Invalid("source has an unknown value.");
        if (!TryParseTypeGroups(request.TypeGroup, out var typeGroups))
            return Invalid("typeGroup has an unknown value.");

        LibraryCursor? cursor = null;
        if (request.Cursor is not null)
        {
            var decoded = LibraryCursor.Decode(request.Cursor, sort);
            if (decoded.IsFailure)
                return Result.Failure<LibraryFilter>(decoded.Error);
            cursor = decoded.Value;
        }

        return WithOwner(
            new LibraryFilter
            {
                Search = search is { Length: >= MinSearchLength } ? search : null,
                Sources = sources,
                CustomerId = request.CustomerId,
                UploadedBy = request.UploadedBy,
                TypeGroups = typeGroups,
                FromUtc = request.From?.UtcDateTime,
                ToUtc = request.To?.UtcDateTime,
                TaxYear = request.TaxYear,
                FileId = request.FileId,
                Sort = sort,
                Take = request.Take ?? DefaultTake,
                Cursor = cursor,
                IncludeCounts = request.Counts ?? cursor is null,
            },
            request,
            forcedSource
        );
    }

    // El dueño solo filtra la búsqueda dentro de un origen; en la biblioteca se ignora.
    private static Result<LibraryFilter> WithOwner(
        LibraryFilter filter,
        LibrarySearchRequest request,
        LibrarySource? forcedSource
    )
    {
        if (forcedSource is null || request.OwnerType is null)
            return Result.Success(forcedSource is null ? filter : filter with { OwnerId = request.OwnerId });
        if (
            !Enum.TryParse<OwnerType>(request.OwnerType, ignoreCase: true, out var ownerType)
            || ownerType is not (Domain.Files.OwnerType.Customer or Domain.Files.OwnerType.Tenant)
        )
            return Invalid("ownerType must be Customer or Tenant.");
        return Result.Success(filter with { OwnerType = ownerType, OwnerId = request.OwnerId });
    }

    private static bool TryParseSort(string? value, out LibrarySort sort)
    {
        sort = LibrarySort.Newest;
        if (string.IsNullOrEmpty(value))
            return true;
        return value.ToLowerInvariant() switch
        {
            "newest" => true,
            "oldest" => Set(LibrarySort.Oldest, out sort),
            "name" => Set(LibrarySort.Name, out sort),
            _ => false,
        };
    }

    private static bool Set(LibrarySort value, out LibrarySort sort)
    {
        sort = value;
        return true;
    }

    private static bool TryParseSources(
        string[]? values,
        LibrarySource? forcedSource,
        out IReadOnlyList<LibrarySource> sources
    )
    {
        sources = forcedSource is { } forced ? [forced] : [];
        if (forcedSource is not null || values is null)
            return true;
        var parsed = new List<LibrarySource>();
        foreach (var value in values)
        {
            if (!LibrarySourceCatalog.TryParse(value, out var source))
                return false;
            if (!parsed.Contains(source))
                parsed.Add(source);
        }
        sources = parsed;
        return true;
    }

    private static bool TryParseTypeGroups(string[]? values, out IReadOnlyList<LibraryTypeGroup> groups)
    {
        groups = [];
        if (values is null)
            return true;
        var parsed = new List<LibraryTypeGroup>();
        foreach (var value in values)
        {
            if (!LibraryTypeGroups.TryParse(value, out var group))
                return false;
            if (!parsed.Contains(group))
                parsed.Add(group);
        }
        groups = parsed;
        return true;
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");

    private static Result<LibraryFilter> Invalid(string message) =>
        Result.Failure<LibraryFilter>(LibraryErrors.InvalidQuery(message));
}

/// <summary>Grupos de tipo sobre el tipo detectado (o el declarado si falta).</summary>
public static class LibraryTypeGroups
{
    private static readonly Dictionary<LibraryTypeGroup, string[]> ContentTypes = new()
    {
        [LibraryTypeGroup.Pdf] = ["application/pdf"],
        [LibraryTypeGroup.Image] =
        [
            "image/jpeg",
            "image/png",
            "image/gif",
            "image/webp",
            "image/tiff",
            "image/svg+xml",
        ],
        [LibraryTypeGroup.Spreadsheet] =
        [
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "text/csv",
        ],
        [LibraryTypeGroup.Document] =
        [
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-powerpoint",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "text/plain",
            "application/rtf",
        ],
        // Los tipos de audio y video que admite el catálogo de subida.
        [LibraryTypeGroup.Media] = ["video/webm", "audio/webm", "video/mp4", "audio/mp4"],
        [LibraryTypeGroup.Other] = [],
    };

    /// <summary>Todos los tipos con grupo propio; el resto es <see cref="LibraryTypeGroup.Other"/>.</summary>
    public static IReadOnlyList<string> Known { get; } = ContentTypes.Values.SelectMany(types => types).ToArray();

    public static IReadOnlyList<string> ContentTypesOf(LibraryTypeGroup group) => ContentTypes[group];

    public static LibraryTypeGroup Of(string contentType) =>
        ContentTypes.FirstOrDefault(entry => entry.Value.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            is { Value: not null } match
            ? match.Key
            : LibraryTypeGroup.Other;

    public static bool TryParse(string? value, out LibraryTypeGroup group) =>
        Enum.TryParse(value, ignoreCase: true, out group)
        && Enum.IsDefined(group)
        && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
}

/// <summary>
/// Cursor opaco de la paginación por clave: <c>base64url</c> de <c>{ "s": orden, "k": clave, "id": guid }</c>.
/// La clave es el instante (ISO-8601 UTC) o el nombre, según el orden.
/// </summary>
public sealed record LibraryCursor(LibrarySort Sort, string Key, Guid Id)
{
    private static readonly string[] SortNames = ["newest", "oldest", "name"];

    /// <summary>Instante de la clave; solo en los órdenes por fecha.</summary>
    public DateTime CreatedAtKey =>
        DateTime.Parse(Key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    public static LibraryCursor After(LibrarySort sort, DateTime createdAtUtc, string name, Guid id) =>
        new(
            sort,
            sort == LibrarySort.Name
                ? name
                : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            id
        );

    public string Encode()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new Payload(SortNames[(int)Sort], Key, Id));
        return Convert.ToBase64String(json).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static Result<LibraryCursor> Decode(string? value, LibrarySort expectedSort)
    {
        var payload = TryRead(value);
        var index = payload?.S is null ? -1 : Array.IndexOf(SortNames, payload.S);
        if (payload?.K is null || payload.Id is not { } id || index < 0)
            return Invalid("cursor is not valid.");
        if ((LibrarySort)index != expectedSort)
            return Invalid("cursor belongs to another sort.");
        if (
            expectedSort != LibrarySort.Name
            && !DateTime.TryParse(payload.K, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)
        )
            return Invalid("cursor is not valid.");
        return Result.Success(new LibraryCursor(expectedSort, payload.K, id));
    }

    private static Payload? TryRead(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            return JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static Result<LibraryCursor> Invalid(string message) =>
        Result.Failure<LibraryCursor>(LibraryErrors.InvalidQuery(message));

    private sealed record Payload(
        [property: JsonPropertyName("s")] string? S,
        [property: JsonPropertyName("k")] string? K,
        [property: JsonPropertyName("id")] Guid? Id
    );
}
