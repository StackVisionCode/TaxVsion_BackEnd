namespace TaxVision.Notes.Domain.Notes;

/// <summary>
/// Adjunto de una nota — entidad hija de <see cref="Note"/>, nunca se crea/muta directamente
/// desde fuera del aggregate (todos los miembros mutables son <c>internal</c>). Flujo (Caso B,
/// ADR-07): el frontend sube a CloudStorage y llama a Notes con el <c>fileId</c> →
/// <see cref="Note.AttachFile"/> crea el adjunto en <see cref="NoteAttachmentStatus.Pending"/>.
/// Los consumers de CloudStorage mueven a <c>Available</c>/<c>Rejected</c>;
/// <see cref="Note.DetachFile"/> mueve a <c>Detached</c> (soft).
/// </summary>
/// <remarks>
/// Guardrail 10: <see cref="Id"/> es un Guid generado en dominio → EF config con
/// <c>ValueGeneratedNever()</c> (Fase 2), o EF hará UPDATE en vez de INSERT al agregar hijos nuevos.
/// </remarks>
public sealed class NoteAttachment
{
    public Guid Id { get; }
    public Guid NoteId { get; }
    public Guid CloudStorageFileId { get; }
    public string DisplayName { get; }
    public string ContentType { get; }
    public long SizeBytes { get; }
    public NoteAttachmentStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime LinkedAtUtc { get; }

    private NoteAttachment(
        Guid id,
        Guid noteId,
        Guid cloudStorageFileId,
        string displayName,
        string contentType,
        long sizeBytes,
        DateTime linkedAtUtc
    )
    {
        Id = id;
        NoteId = noteId;
        CloudStorageFileId = cloudStorageFileId;
        DisplayName = displayName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Status = NoteAttachmentStatus.Pending;
        LinkedAtUtc = linkedAtUtc;
    }

    internal static NoteAttachment Create(
        Guid noteId,
        Guid cloudStorageFileId,
        string displayName,
        string contentType,
        long sizeBytes,
        DateTime linkedAtUtc
    ) => new(Guid.NewGuid(), noteId, cloudStorageFileId, displayName, contentType, sizeBytes, linkedAtUtc);

    /// <summary>Idempotente: no-op si ya está <see cref="NoteAttachmentStatus.Available"/>.</summary>
    internal void MarkAvailable()
    {
        if (Status == NoteAttachmentStatus.Available)
            return;
        Status = NoteAttachmentStatus.Available;
    }

    /// <summary>Idempotente: no-op si ya está <see cref="NoteAttachmentStatus.Rejected"/> con la misma razón.</summary>
    internal void MarkRejected(string reason)
    {
        if (Status == NoteAttachmentStatus.Rejected && RejectionReason == reason)
            return;
        Status = NoteAttachmentStatus.Rejected;
        RejectionReason = reason;
    }

    /// <summary>Idempotente: no-op si ya está <see cref="NoteAttachmentStatus.Detached"/>.</summary>
    internal void MarkDetached()
    {
        if (Status == NoteAttachmentStatus.Detached)
            return;
        Status = NoteAttachmentStatus.Detached;
    }
}
