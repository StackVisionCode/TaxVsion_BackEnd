namespace TaxVision.CloudStorage.Domain.Files;

/// <summary>
/// Categoría visual por extensión, la misma regla que <c>categoryFromFileName</c> del CRM: sin punto, el nombre
/// entero es la extensión. Es la única fuente del SQL de la columna calculada <c>Files.Category</c>; cambiar una
/// lista exige migración.
/// </summary>
public static class StorageCategories
{
    public const string Documents = "Documents";
    public const string Images = "Images";
    public const string Media = "Video & Audio";
    public const string Others = "Others";
    public const int MaxLength = 16;

    public static IReadOnlyList<string> All { get; } = [Documents, Images, Media, Others];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Extensions { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [Documents] =
            [
                "pdf",
                "doc",
                "docx",
                "xls",
                "xlsx",
                "ppt",
                "pptx",
                "txt",
                "csv",
                "rtf",
                "xml",
                "json",
                "html",
                "md",
            ],
            [Images] = ["jpg", "jpeg", "png", "gif", "webp", "tif", "tiff", "bmp", "svg", "heic"],
            [Media] = ["mp3", "wav", "m4a", "ogg", "aac", "flac", "mp4", "webm", "mov", "avi", "mkv"],
        };

    public static string ComputedColumnSql { get; } = BuildSql();

    private static string BuildSql()
    {
        const string extension =
            "LOWER(CASE WHEN CHARINDEX(N'.', [OriginalName]) = 0 THEN [OriginalName] "
            + "ELSE RIGHT([OriginalName], CHARINDEX(N'.', REVERSE([OriginalName])) - 1) END)";
        // Orden fijo de All: el SQL tiene que salir idéntico en cada build.
        var branches = All.Where(Extensions.ContainsKey)
            .Select(category =>
                $"WHEN {extension} IN ({string.Join(", ", Extensions[category].Select(e => $"N'{e}'"))}) THEN N'{category}'"
            );
        return $"CAST(CASE {string.Join(" ", branches)} ELSE N'{Others}' END AS nvarchar({MaxLength}))";
    }
}
