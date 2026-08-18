namespace BuildingBlocks.Messaging.NotesIntegrationEvents;

// ---------------------------------------------------------------------------
// Notes Fase 5 (02_Contratos_Integracion_Y_Proyecciones.md §1.1, 01_Modelo_De_Dominio.md §5) —
// eventos mínimos para MyPlanner (BFF) y un futuro índice de búsqueda. Deliberadamente sin
// PII/contenido completo: solo IDs + metadata — quien consuma resuelve el contenido leyendo
// Notes directamente (respetando visibilidad), nunca desde el evento.
// ---------------------------------------------------------------------------

/// <summary><c>notes.note_created.v1</c> — publicado tras crear una nota.</summary>
public sealed record NoteCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid NoteId { get; init; }
    public required Guid AuthorUserId { get; init; }
    public required string TargetType { get; init; }
    public Guid? TargetId { get; init; }
    public required string Visibility { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}

/// <summary><c>notes.note_updated.v1</c> — publicado tras cambiar contenido/visibilidad/pin de una nota.</summary>
public sealed record NoteUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid NoteId { get; init; }
    public required string Visibility { get; init; }
    public required bool IsPinned { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}

/// <summary><c>notes.note_deleted.v1</c> — publicado tras el soft-delete de una nota.</summary>
public sealed record NoteDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid NoteId { get; init; }
}

/// <summary><c>notes.attachment_detached.v1</c> — publicado al desvincular un adjunto (el byte lo recoge la retención de CloudStorage, ADR-07).</summary>
public sealed record NoteAttachmentDetachedIntegrationEvent : IntegrationEvent
{
    public required Guid NoteId { get; init; }
    public required Guid CloudStorageFileId { get; init; }
}
