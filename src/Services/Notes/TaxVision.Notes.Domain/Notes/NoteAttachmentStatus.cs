namespace TaxVision.Notes.Domain.Notes;

/// <summary>
/// Ciclo de vida de <see cref="NoteAttachment"/> (Caso B, ADR-07): el frontend sube a CloudStorage
/// y llama a Notes con el <c>fileId</c> → nace en <c>Pending</c>. Los consumers de CloudStorage
/// (<c>FileAvailable</c>/<c>FileInfectedDetected</c>/<c>FileBlockedByPolicy</c>) mueven a
/// <c>Available</c>/<c>Rejected</c>. <c>DetachFile</c> mueve a <c>Detached</c> (soft delete).
/// </summary>
public enum NoteAttachmentStatus
{
    Pending = 0,
    Available = 1,
    Rejected = 2,
    Detached = 3,
}
