using System.Linq.Expressions;
using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
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
    public async Task<IReadOnlyList<Note>> ListByAttachmentFileIdAsync(
        Guid cloudStorageFileId,
        CancellationToken ct = default
    ) =>
        await db
            .Notes.IgnoreQueryFilters()
            .Include(n => n.Attachments)
            .Where(n => n.Attachments.Any(a => a.CloudStorageFileId == cloudStorageFileId))
            .ToListAsync(ct);

    public async Task<PagedResult<Note>> ListByReferenceAsync(
        Guid tenantId,
        NoteTargetType targetType,
        Guid targetId,
        Guid actorUserId,
        bool actorHasViewAll,
        int page,
        int size,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    )
    {
        var baseQuery = VisibleTo(tenantId, actorUserId, actorHasViewAll)
            .Where(n => n.Reference.TargetType == targetType && n.Reference.TargetId == targetId);

        var query = ApplyAssignmentFilter(baseQuery, tenantId, assignedToUserId)
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
        // IgnoreQueryFilters(): ver comentario en VisibleTo.
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
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    )
    {
        var baseQuery = VisibleTo(tenantId, actorUserId, actorHasViewAll).Where(MatchesTerm(term));

        var query = ApplyAssignmentFilter(baseQuery, tenantId, assignedToUserId).OrderByDescending(n => n.UpdatedAtUtc);

        return await ToPagedResultAsync(query, page, size, ct);
    }

    public async Task<PagedResult<NoteFeedRow>> ListFeedAsync(
        Guid tenantId,
        Guid actorUserId,
        bool actorHasViewAll,
        Guid? assignedToUserId,
        NoteFeedCriteria criteria,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var visible = ApplyAssignmentFilter(
            VisibleTo(tenantId, actorUserId, actorHasViewAll),
            tenantId,
            assignedToUserId
        );
        var filtered = ApplyFeedCriteria(visible, actorUserId, criteria);
        var totalCount = await filtered.CountAsync(ct);

        // Nombre del cliente en la misma consulta (left join al directorio local, sin N+1).
        var directory = db.CustomerDirectoryEntries.IgnoreQueryFilters().Where(c => c.TenantId == tenantId);
        var rows =
            from n in filtered.Include(n => n.Attachments)
            join c in directory on n.Reference.TargetId equals (Guid?)c.CustomerId into matches
            from c in matches.DefaultIfEmpty()
            select new
            {
                Note = n,
                CustomerName = n.Reference.TargetType == NoteTargetType.Customer && c != null ? c.DisplayName : null,
            };

        var ordered = criteria.Sort switch
        {
            NoteFeedSort.Created => rows.OrderByDescending(r => r.Note.CreatedAtUtc),
            NoteFeedSort.Pinned => rows.OrderByDescending(r => r.Note.IsPinned)
                .ThenByDescending(r => r.Note.UpdatedAtUtc),
            _ => rows.OrderByDescending(r => r.Note.UpdatedAtUtc),
        };
        var pageRows = await ordered
            .ThenByDescending(r => r.Note.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var items = pageRows.Select(r => new NoteFeedRow(r.Note, r.CustomerName)).ToList();
        return new PagedResult<NoteFeedRow>(items, page, size, totalCount);
    }

    public async Task<IReadOnlyList<NoteTitleRow>> ListTitlesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        Guid actorUserId,
        bool actorHasViewAll,
        Guid? assignedToUserId,
        CancellationToken ct = default
    )
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0)
            return [];

        // Solo el preview: alcanza para un título de una línea sin traer el texto completo.
        return await ApplyAssignmentFilter(
                VisibleTo(tenantId, actorUserId, actorHasViewAll),
                tenantId,
                assignedToUserId
            )
            .Where(n => wanted.Contains(n.Id))
            .Select(n => new NoteTitleRow(n.Id, n.Content.PlainTextPreview))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<NoteAttachmentFileRow>> ListVisibleAttachmentFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        Guid actorUserId,
        bool actorHasViewAll,
        Guid? assignedToUserId,
        CancellationToken ct = default
    )
    {
        var wanted = fileIds.Distinct().ToList();
        if (wanted.Count == 0)
            return [];

        return await ApplyAssignmentFilter(
                VisibleTo(tenantId, actorUserId, actorHasViewAll),
                tenantId,
                assignedToUserId
            )
            .SelectMany(
                n =>
                    n.Attachments.Where(a =>
                        a.Status == NoteAttachmentStatus.Available && wanted.Contains(a.CloudStorageFileId)
                    ),
                (n, a) =>
                    new NoteAttachmentFileRow(
                        a.CloudStorageFileId,
                        n.Id,
                        n.Content.PlainTextPreview,
                        n.Visibility,
                        n.CreatedByUserId,
                        n.Reference.TargetType,
                        n.Reference.TargetId,
                        a.LinkedAtUtc
                    )
            )
            .ToListAsync(ct);
    }

    // Predicado de visibilidad de staff común a los listados: tenant explícito, sin borradas y privadas solo del
    // autor o con notes.view_all (NoteVisibilityPolicy.CanStaffView en SQL). IgnoreQueryFilters(): el filtro
    // ambiental por tenant no está garantizado en el scope de DI con que Wolverine despacha la query (Fase 10:
    // sin esto los listados devolvían 0 filas); el tenant llega explícito desde Application.
    private IQueryable<Note> VisibleTo(Guid tenantId, Guid actorUserId, bool actorHasViewAll) =>
        db
            .Notes.IgnoreQueryFilters()
            .Where(n =>
                n.TenantId == tenantId
                && n.Status != NoteStatus.Deleted
                && (n.Visibility != NoteVisibility.Private || n.CreatedByUserId == actorUserId || actorHasViewAll)
            );

    // Texto plano completo; las notas aún sin backfill caen al preview.
    private static Expression<Func<Note, bool>> MatchesTerm(string term) =>
        n => (n.Content.PlainText ?? n.Content.PlainTextPreview).Contains(term);

    private static IQueryable<Note> ApplyFeedCriteria(
        IQueryable<Note> query,
        Guid actorUserId,
        NoteFeedCriteria criteria
    )
    {
        var status = criteria.Status == NoteFeedStatus.Archived ? NoteStatus.Archived : NoteStatus.Active;
        query = query.Where(n => n.Status == status);
        query = criteria.Scope switch
        {
            NoteFeedScope.Mine => query.Where(n => n.CreatedByUserId == actorUserId),
            NoteFeedScope.Team => query.Where(n => n.Visibility == NoteVisibility.Team),
            NoteFeedScope.Client => query.Where(n => n.Visibility == NoteVisibility.ClientVisible),
            NoteFeedScope.Private => query.Where(n =>
                n.Visibility == NoteVisibility.Private && n.CreatedByUserId == actorUserId
            ),
            NoteFeedScope.OthersPrivate => query.Where(n =>
                n.Visibility == NoteVisibility.Private && n.CreatedByUserId != actorUserId
            ),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(criteria.Term))
            query = query.Where(MatchesTerm(criteria.Term.Trim()));
        if (criteria.CustomerId is { } customerId)
            query = query.Where(n =>
                n.Reference.TargetType == NoteTargetType.Customer && n.Reference.TargetId == customerId
            );
        if (criteria.AuthorUserId is { } authorUserId)
            query = query.Where(n => n.CreatedByUserId == authorUserId);
        if (criteria.TargetType is { } targetType)
            query = query.Where(n => n.Reference.TargetType == targetType);
        if (criteria.FromUtc is { } fromUtc)
            query = query.Where(n => n.UpdatedAtUtc >= fromUtc);
        if (criteria.ToUtc is { } toUtc)
            query = query.Where(n => n.UpdatedAtUtc < toUtc);
        return query;
    }

    // Visibilidad por asignación (P2): una nota cuyo target es un Customer solo es visible si ese cliente
    // está asignado al actor en la proyección local. Las notas de OTROS targets (Task/Appointment/None/…)
    // no se restringen — el eje es cliente. IgnoreQueryFilters + tenant explícito en el subquery (scope de
    // Wolverine sin tenant ambiental). null = sin restricción (customers.view_all / flag off).
    private IQueryable<Note> ApplyAssignmentFilter(IQueryable<Note> query, Guid tenantId, Guid? assignedToUserId)
    {
        if (assignedToUserId is not { } assignee)
            return query;
        return query.Where(n =>
            n.Reference.TargetType != NoteTargetType.Customer
            || db.Set<CustomerAssignmentProjection>()
                .IgnoreQueryFilters()
                .Any(a => a.TenantId == tenantId && a.UserId == assignee && a.CustomerId == n.Reference.TargetId)
        );
    }

    public Task<bool> IsCustomerAssignedAsync(
        Guid tenantId,
        Guid customerId,
        Guid actorUserId,
        CancellationToken ct = default
    ) =>
        db.Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .AnyAsync(a => a.TenantId == tenantId && a.UserId == actorUserId && a.CustomerId == customerId, ct);

    public async Task<PagedResult<Note>> ListClientVisibleAsync(
        Guid tenantId,
        NoteTargetType targetType,
        Guid targetId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        // IgnoreQueryFilters(): ver comentario en VisibleTo. Este método además lo invoca
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
