using BuildingBlocks.Common;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes.Abstractions;

/// <summary>
/// Repositorio del aggregate root <see cref="Note"/>. Todas las lecturas por tenant filtran por
/// <c>TenantId</c> explícito (el aislamiento multitenant se hace a nivel de repo, nunca se acepta
/// una query sin tenant) — la única excepción es <see cref="GetByAttachmentFileIdAsync"/>, que la
/// invoca el consumer de CloudStorage sin tenant ambiental (ver comentario del método).
/// </summary>
public interface INoteRepository
{
    /// <summary>Busca por <c>(TenantId, Id)</c> con adjuntos incluidos. Devuelve <c>null</c> si no existe o pertenece a otro tenant.</summary>
    Task<Note?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Busca la nota dueña de un adjunto por <c>CloudStorageFileId</c>, sin filtro de tenant — la
    /// invocan los consumers de <c>FileAvailable</c>/<c>FileInfectedDetected</c>/<c>FileBlockedByPolicy</c>,
    /// que no corren dentro de un scope con tenant ambiental (Wolverine consumer). El caller debe
    /// validar <c>note.TenantId == evt.TenantId</c> después del fetch (guardrail 8).
    /// </summary>
    Task<Note?> GetByAttachmentFileIdAsync(Guid cloudStorageFileId, CancellationToken ct = default);

    /// <summary>
    /// Notas de un target polimórfico (p.ej. todas las notas de un Customer), más recientes
    /// primero — filtro de visibilidad (Fase 5, <c>NoteVisibilityPolicy.CanStaffView</c>) aplicado
    /// a nivel SQL: excluye <c>Private</c> ajenas salvo <paramref name="actorHasViewAll"/>. Empujar
    /// el filtro al query evita el bug clásico de paginar-y-luego-filtrar-en-memoria (TotalCount y
    /// tamaño de página quedarían mal si se filtrara después de traer la página).
    /// </summary>
    Task<PagedResult<Note>> ListByReferenceAsync(
        Guid tenantId,
        NoteTargetType targetType,
        Guid targetId,
        Guid actorUserId,
        bool actorHasViewAll,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>"Mis notas" — todas las creadas por un autor concreto, más recientes primero. Sin filtro de visibilidad: el autor siempre ve sus propias notas.</summary>
    Task<PagedResult<Note>> ListForAuthorAsync(
        Guid tenantId,
        Guid authorUserId,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>Búsqueda simple sobre <c>ContentPreview</c> (categoría H — sin full-text en v1). Mismo filtro de visibilidad SQL que <see cref="ListByReferenceAsync"/>.</summary>
    Task<PagedResult<Note>> SearchAsync(
        Guid tenantId,
        string term,
        Guid actorUserId,
        bool actorHasViewAll,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// CustomerPortal (<c>ListClientVisibleNotesQuery</c>): solo <see cref="NoteVisibility.ClientVisible"/>,
    /// nunca <c>Private</c>/<c>Team</c>/<c>Deleted</c> — filtro más estricto que
    /// <see cref="ListByReferenceAsync"/>, no reutilizable con él (un portal jamás tiene
    /// <c>actorHasViewAll</c> ni pasa por el chequeo de autoría).
    /// </summary>
    Task<PagedResult<Note>> ListClientVisibleAsync(
        Guid tenantId,
        NoteTargetType targetType,
        Guid targetId,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task AddAsync(Note note, CancellationToken ct = default);
}
