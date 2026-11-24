using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Domain.Providers;

/// <summary>
/// Dónde queda la copia de un archivo en el proveedor externo: <c>{marca}/{categoría}[/{año}]/{nombre}</c>.
/// Siempre válida: carpetas legibles, sin GUIDs ni ids internos, y nombres que aceptan Drive, OneDrive y Dropbox.
/// </summary>
public sealed record CloudMirrorPath
{
    public const string DefaultRootFolder = "Files";
    public const int MaxFileNameLength = 200;
    private const int MaxFolderNameLength = 100;
    private const string FallbackFileName = "file";

    // Lo que rechaza alguno de los tres proveedores (OneDrive es el más estricto).
    private static readonly HashSet<char> InvalidCharacters = [.. "\\/:*?\"<>|#%"];

    private CloudMirrorPath(IReadOnlyList<string> folders, string fileName)
    {
        Folders = folders;
        FileName = fileName;
    }

    public IReadOnlyList<string> Folders { get; }
    public string FileName { get; }
    public string FolderPath => string.Join('/', Folders);
    public string FullPath => $"{FolderPath}/{FileName}";

    public static CloudMirrorPath For(string brandName, FolderType folderType, int? taxYear, string originalName)
    {
        var folders = new List<string> { Clean(brandName, MaxFolderNameLength) ?? DefaultRootFolder };
        folders.AddRange(CategoryOf(folderType));
        if (taxYear is { } year)
            folders.Add(year.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new CloudMirrorPath(folders, FileNameOf(originalName));
    }

    /// <summary>Carpetas en inglés, como el resto de la interfaz. El tipo de un archivo no cambia, así que su carpeta tampoco.</summary>
    private static string[] CategoryOf(FolderType folderType) =>
        folderType switch
        {
            FolderType.Documents => ["Documents"],
            FolderType.Receipts => ["Receipts"],
            FolderType.Invoices => ["Invoices"],
            FolderType.EmailIncoming => ["Email attachments", "Received"],
            FolderType.EmailOutgoing => ["Email attachments", "Sent"],
            FolderType.Tasks => ["Tasks"],
            FolderType.Signatures => ["Signatures"],
            FolderType.Imports => ["Imports"],
            FolderType.Recordings => ["Recordings"],
            FolderType.Transcripts => ["Transcripts"],
            FolderType.Backups => ["Backups"],
            FolderType.Templates => ["Templates"],
            FolderType.Branding => ["Branding"],
            FolderType.VoiceNotes => ["Voice notes"],
            FolderType.SaaSReceipts => ["Subscription receipts"],
            FolderType.ClientForms => ["Client forms"],
            _ => ["Other"],
        };

    private static string FileNameOf(string originalName)
    {
        var cleaned = Clean(originalName, int.MaxValue) ?? FallbackFileName;
        if (cleaned.Length <= MaxFileNameLength)
            return cleaned;
        // Recorta el nombre y conserva la extensión (si es razonable).
        var extension = Path.GetExtension(cleaned);
        if (extension.Length is 0 or > 16)
            extension = string.Empty;
        return cleaned[..(MaxFileNameLength - extension.Length)].TrimEnd(' ', '.') + extension;
    }

    /// <summary>Reemplaza lo no permitido por '_' y quita espacios y puntos de los extremos. Null si no queda nada.</summary>
    private static string? Clean(string? value, int maxLength)
    {
        var replaced = new string(
            (value ?? string.Empty).Select(c => char.IsControl(c) || InvalidCharacters.Contains(c) ? '_' : c).ToArray()
        ).Trim(' ', '.');
        if (replaced.Length == 0)
            return null;
        return replaced.Length <= maxLength ? replaced : replaced[..maxLength].TrimEnd(' ', '.');
    }
}
