using BuildingBlocks.Authorization;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Application.Abstractions;

/// <summary>Módulos cuyos archivos exigen su propio permiso de lectura además del de CloudStorage.</summary>
public enum FileReadSurface
{
    Billing,
    Signature,
}

/// <summary>
/// Compuerta de lectura por origen: el PDF de una factura o un documento de firma solo se abre con el
/// permiso de su módulo. El resto de dueños sigue con la regla de CloudStorage.
/// </summary>
public static class FileReadGate
{
    // Billing no publica sus constantes en BuildingBlocks.
    private const string InvoicingView = "invoicing.view";

    public static IReadOnlyList<FileReadSurface> All { get; } = [FileReadSurface.Billing, FileReadSurface.Signature];

    public static FileReadSurface? SurfaceOf(OwnerType ownerType) =>
        ownerType switch
        {
            OwnerType.Invoice => FileReadSurface.Billing,
            OwnerType.Signature => FileReadSurface.Signature,
            _ => null,
        };

    // Firma: también la abren las pantallas de firma del preparador y de ajustes.
    public static IReadOnlyList<string> PermissionsOf(FileReadSurface surface) =>
        surface switch
        {
            FileReadSurface.Billing => [InvoicingView],
            FileReadSurface.Signature =>
            [
                SignaturePermissions.RequestRead,
                SignaturePermissions.PreparerManage,
                SignaturePermissions.SettingsManage,
            ],
            _ => [],
        };

    /// <summary>Carpeta de sistema de las notas de voz: se cierra con sus archivos.</summary>
    public const string VoiceNotesFolderCategory = "sys.voicenotes";

    /// <summary>Recurso de los adjuntos que declara Notes: los entrega Notes por la nota.</summary>
    public const string NoteResourceType = "note";

    /// <summary>
    /// Archivos que solo entrega su dueño (Communication por la conversación o la llamada; Notes por la nota;
    /// Correspondence el adjunto entrante sin cliente). Para una persona no existen en CloudStorage. Las fotos de
    /// grupo se sirven por /storage/avatars.
    /// </summary>
    public static bool IsOwnerMediated(FileObject file) =>
        (file.OwnerType == OwnerType.Communication && file.FolderType != FolderType.Avatars)
        || file.FolderType == FolderType.VoiceNotes
        || file.SourceResourceType == NoteResourceType
        || (file.OwnerType == OwnerType.User && file.FolderType == FolderType.EmailIncoming);

    public static bool IsOwnerMediated(Folder folder) => folder.Category == VoiceNotesFolderCategory;
}
