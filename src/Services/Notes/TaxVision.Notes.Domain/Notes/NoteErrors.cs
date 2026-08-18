using BuildingBlocks.Results;

namespace TaxVision.Notes.Domain.Notes;

/// <summary>
/// Errores de dominio de <see cref="Note"/>. <see cref="NotFound"/>/<see cref="Forbidden"/> se usan
/// en Application (post-fetch tenant/owner checks, guardrail 8), nunca dentro del aggregate.
/// </summary>
public static class NoteErrors
{
    public static readonly Error Deleted = new("Note.Deleted", "La nota fue eliminada.");
    public static readonly Error ReferenceTargetRequired = new(
        "Note.ReferenceTargetRequired",
        "El tipo de referencia requiere un TargetId."
    );
    public static readonly Error ContentEmpty = new("Note.ContentEmpty", "El contenido no puede estar vacío.");
    public static readonly Error ContentTooLong = new("Note.ContentTooLong", "El contenido excede el máximo.");
    public static readonly Error AttachmentDuplicate = new("Note.AttachmentDuplicate", "El archivo ya está adjunto.");
    public static readonly Error AttachmentLimit = new("Note.AttachmentLimit", "Se excedió el máximo de adjuntos.");
    public static readonly Error AttachmentNotFound = new("Note.AttachmentNotFound", "Adjunto no encontrado.");
    public static readonly Error InvalidTransition = new("Note.InvalidTransition", "Transición de estado inválida.");
    public static readonly Error NotFound = new("Note.NotFound", "Nota no encontrada.");
    public static readonly Error Forbidden = new("Note.Forbidden", "No autorizado sobre esta nota.");
}
