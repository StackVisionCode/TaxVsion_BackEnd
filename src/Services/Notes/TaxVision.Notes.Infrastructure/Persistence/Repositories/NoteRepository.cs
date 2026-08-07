using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Infrastructure.Persistence.Repositories;

public sealed class NoteRepository(NotesDbContext db) : INoteRepository
{
    // tenantId ya viene explícito y validado desde el caller (Application) — IgnoreQueryFilters()
    // porque el filtro ambiental global puede no estar poblado en el scope de DI del handler de
    // Wolverine (mismo patrón que SignatureRequestRepository.GetByIdAsync).
    public Task<Note?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .Notes.IgnoreQueryFilters()
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(n => n.Id == id && n.TenantId == tenantId, ct);

    // Sin tenant ambiental: lo invoca el consumer de CloudStorage (Fase 7). El caller valida
    // note.TenantId == evt.TenantId después del fetch.
    public Task<Note?> GetByAttachmentFileIdAsync(Guid cloudStorageFileId, CancellationToken ct = default) =>
        db
            .Notes.IgnoreQueryFilters()
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(n => n.Attachments.Any(a => a.CloudStorageFileId == cloudStorageFileId), ct);

    public async Task<PagedResult<Note>> ListByReferenceAsync(
        Guid tenantId,
        NoteTargetType targetType,
        Guid targetId,
        Guid actorUserId,
        bool actorHasViewAll,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        // IgnoreQueryFilters(): mismo motivo que GetByIdAsync — tenantId ya viene explícito y
        // validado desde el caller (Application), y el filtro ambiental global (JwtTenantContextMiddleware
        // vía ITenantContext) no está garantizado poblado en el scope de DI que usa Wolverine para
        // despachar localmente esta query (bug real encontrado en verificación E2E en vivo, Fase 10:
        // sin este fix, ListByReferenceAsync/ListForAuthorAsync/SearchAsync/ListClientVisibleAsync
        // devolvían siempre 0 filas porque el filtro fail-closed comparaba contra Guid.Empty).
        var query = db
            .Notes.IgnoreQueryFilters()
            .Where(n =>
                n.TenantId == tenantId
                && n.Status != NoteStatus.Deleted
                && n.Reference.TargetType == targetType
                && n.Reference.TargetId == targetId
                && (n.Visibility != NoteVisibility.Private || n.CreatedByUserId == actorUserId || actorHasViewAll)
            )
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.UpdatedAtUtc);

        return await ToPagedResultAsync(query, page, size, ct);
    }

    public async Task<PagedResult<Note>> ListForAuthorAsync(
        Guid tenantId,
        Guid authorUserId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        // IgnoreQueryFilters(): ver comentario en ListByReferenceAsync.
        var query = db
            .Notes.IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.Status != NoteStatus.Deleted && n.CreatedByUserId == authorUserId)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.UpdatedAtUtc);

        return await ToPagedResultAsync(query, page, size, ct);
    }

    public async Task<PagedResult<Note>> SearchAsync(
        Guid tenantId,
        string term,
        Guid actorUserId,
        bool actorHasViewAll,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        // IgnoreQueryFilters(): ver comentario en ListByReferenceAsync.
        var query = db
            .Notes.IgnoreQueryFilters()
            .Where(n =>
                n.TenantId == tenantId
                && n.Status != NoteStatus.Deleted
                && n.Content.PlainTextPreview.Contains(term)
                && (n.Visibility != NoteVisibility.Private || n.CreatedByUserId == actorUserId || actorHasViewAll)
            )
            .OrderByDescending(n => n.UpdatedAtUtc);

        return await ToPagedResultAsync(query, page, size, ct);
    }

    public async Task<PagedResult<Note>> ListClientVisibleAsync(
        Guid tenantId,
        NoteTargetType targetType,
        Guid targetId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        // IgnoreQueryFilters(): ver comentario en ListByReferenceAsync. Este método además lo invoca
        // PortalNotesController (CustomerPortal, actor distinto de staff) por el mismo motivo.
        var query = db
            .Notes.IgnoreQueryFilters()
            .Where(n =>
                n.TenantId == tenantId
                && n.Status != NoteStatus.Deleted
                && n.Reference.TargetType == targetType
                && n.Reference.TargetId == targetId
                && n.Visibility == NoteVisibility.ClientVisible
            )
            .OrderByDescending(n => n.UpdatedAtUtc);

        return await ToPagedResultAsync(query, page, size, ct);
    }

    public async Task AddAsync(Note note, CancellationToken ct = default) => await db.Notes.AddAsync(note, ct);

    private static async Task<PagedResult<Note>> ToPagedResultAsync(
        IQueryable<Note> query,
        int page,
        int size,
        CancellationToken ct
    )
    {
        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).Include(n => n.Attachments).ToListAsync(ct);
        return new PagedResult<Note>(items, page, size, totalCount);
    }
}
