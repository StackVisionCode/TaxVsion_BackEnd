using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Folders;

/// <summary>Nombre visible + category ancla de una carpeta de sistema.</summary>
public readonly record struct SystemFolderSpec(string Name, string Category);

/// <summary>
/// Mapea cada <see cref="FolderType"/> DE CARA AL USUARIO a la carpeta navegable
/// canonica donde deben aterrizar sus archivos. Los FolderType internos (Branding,
/// Avatars, Templates, Backups, Recordings, Transcripts, Imports, Other) NO estan en
/// el mapa: sus archivos se quedan en la raiz y el explorer los oculta por tipo — es
/// una clasificacion en codigo, no un flag por fila.
///
/// El <c>Category</c> (prefijo <c>sys.</c>) es el ancla estable del get-or-create
/// (indice unico IX_Folders_Owner_Category): renombrar el <c>Name</c> visible nunca
/// rompe la identidad de la carpeta. EmailIncoming y EmailOutgoing comparten la misma
/// carpeta ("Email").
/// </summary>
public static class SystemFolderCatalog
{
    private static readonly IReadOnlyDictionary<FolderType, SystemFolderSpec> Map = new Dictionary<
        FolderType,
        SystemFolderSpec
    >
    {
        [FolderType.Documents] = new("Documents", "sys.documents"),
        [FolderType.Receipts] = new("Receipts", "sys.receipts"),
        [FolderType.Invoices] = new("Invoices", "sys.invoices"),
        [FolderType.EmailIncoming] = new("Email", "sys.email"),
        [FolderType.EmailOutgoing] = new("Email", "sys.email"),
        [FolderType.Tasks] = new("Task Documents", "sys.tasks"),
        [FolderType.Signatures] = new("Signed Documents", "sys.signatures"),
        [FolderType.VoiceNotes] = new("Voice Notes", "sys.voicenotes"),
    };

    /// <summary>Devuelve la carpeta de sistema de un tipo navegable, o false si es interno.</summary>
    public static bool TryGet(FolderType folderType, out SystemFolderSpec spec) =>
        Map.TryGetValue(folderType, out spec);

    /// <summary>true si el tipo es de cara al usuario (tiene carpeta navegable).</summary>
    public static bool IsNavigable(FolderType folderType) => Map.ContainsKey(folderType);

    /// <summary>Todos los FolderType de cara al usuario (los que generan carpeta navegable). Usado por el backfill.</summary>
    public static IReadOnlyCollection<FolderType> NavigableTypes { get; } = Map.Keys.ToArray();

    private static readonly HashSet<string> SystemCategories = Map.Values.Select(spec => spec.Category).ToHashSet();

    /// <summary>
    /// true si la category es la de una carpeta de sistema (prefijo <c>sys.</c> del catalogo). Las
    /// carpetas de sistema no se pueden renombrar/mover/borrar ni un usuario puede crear una con
    /// esa category — solo el provisioner las materializa.
    /// </summary>
    public static bool IsSystemCategory(string? category) =>
        !string.IsNullOrEmpty(category) && SystemCategories.Contains(category);
}
