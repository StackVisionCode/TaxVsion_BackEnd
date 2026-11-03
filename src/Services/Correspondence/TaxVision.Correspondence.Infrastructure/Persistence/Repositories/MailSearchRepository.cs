using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

/// <summary>
/// Búsqueda unificada sobre las mismas reglas de <see cref="MailVisibilityQueries"/>: cada fuente se acota por
/// tenant y por <see cref="MailSearchCriteria.Limit"/> en SQL; la mezcla y el corte final se hacen en memoria
/// (como mucho cuatro veces el límite). Los filtros (cliente, sin cliente, adjuntos, no leídos, fechas) también van
/// en SQL: filtrar en memoria después del límite devolvería resultados incompletos.
/// </summary>
public sealed class MailSearchRepository(CorrespondenceDbContext db) : IMailSearchRepository
{
    public async Task<IReadOnlyList<EmailSearchHit>> SearchEmailsAsync(
        MailSearchCriteria criteria,
        CancellationToken ct = default
    )
    {
        var hits = new List<EmailSearchHit>();
        hits.AddRange(await SearchConversationsAsync(criteria, ct));
        // Un entrante siempre es de un cliente: "sin cliente" no tiene entrantes.
        if (!criteria.Unlinked)
            hits.AddRange(await SearchTrashedIncomingAsync(criteria, ct));
        // Lo saliente nunca está "sin leer".
        if (!criteria.UnreadOnly)
        {
            hits.AddRange(await SearchOutgoingAsync(criteria, DraftStatus.Sent, ct));
            hits.AddRange(await SearchOutgoingAsync(criteria, DraftStatus.Draft, ct));
        }
        return hits.OrderByDescending(h => h.OccurredAtUtc).Take(criteria.Limit).ToList();
    }

    public async Task<IReadOnlyList<FileSearchHit>> SearchFilesAsync(
        MailSearchCriteria criteria,
        CancellationToken ct = default
    )
    {
        var hits = new List<FileSearchHit>();
        if (!criteria.Unlinked)
            hits.AddRange(await SearchIncomingAttachmentsAsync(criteria, ct));
        hits.AddRange(await SearchOutgoingAttachmentsAsync(criteria, ct));
        return hits.OrderByDescending(h => h.OccurredAtUtc).Take(criteria.Limit).ToList();
    }

    // Inbox/Archived: una fila por conversación, por su asunto o por remitente/vista previa de un entrante vivo.
    private async Task<IReadOnlyList<EmailSearchHit>> SearchConversationsAsync(
        MailSearchCriteria criteria,
        CancellationToken ct
    )
    {
        var term = criteria.Term;
        var threads = FilteredThreads(criteria);

        var byIncoming = await (
            from ie in db.IncomingEmails.AsNoTracking().IgnoreQueryFilters()
            join t in threads on ie.EmailThreadId equals t.Id
            where
                ie.TenantId == criteria.TenantId
                && ie.DeletedAtUtc == null
                && (criteria.FromUtc == null || ie.ReceivedAtUtc >= criteria.FromUtc)
                && (criteria.ToUtc == null || ie.ReceivedAtUtc <= criteria.ToUtc)
                && (
                    ie.From.Contains(term)
                    || (ie.FromDisplayName != null && ie.FromDisplayName.Contains(term))
                    || ie.Snippet.Contains(term)
                )
            orderby ie.ReceivedAtUtc descending
            select new
            {
                ie.Id,
                ThreadId = t.Id,
                t.CustomerId,
                t.Subject,
                t.Status,
                ie.From,
                ie.FromDisplayName,
                ie.Snippet,
                ie.ReceivedAtUtc,
                ie.HasAttachments,
            }
        )
            .Take(criteria.Limit)
            .ToListAsync(ct);

        var bySubject = await MailVisibilityQueries
            .ThreadsInRange(threads, criteria.FromUtc, criteria.ToUtc)
            .Where(t => t.Subject.Contains(term))
            .OrderByDescending(t => t.LastMessageAtUtc)
            .Take(criteria.Limit)
            .Select(t => new
            {
                t.Id,
                t.CustomerId,
                t.Subject,
                t.Status,
                t.LastMessageAtUtc,
            })
            .ToListAsync(ct);

        var hits = new Dictionary<Guid, EmailSearchHit>();
        foreach (var row in byIncoming)
        {
            if (hits.ContainsKey(row.ThreadId))
                continue;
            var matched =
                ContainsIgnoreCase(row.From, term) || ContainsIgnoreCase(row.FromDisplayName, term)
                    ? MailSearchValues.MatchedFrom
                    : MailSearchValues.MatchedSnippet;
            hits[row.ThreadId] = new EmailSearchHit(
                MailSearchValues.SourceThread,
                ThreadFolder(row.Status),
                row.ThreadId,
                row.Id,
                row.CustomerId,
                row.Subject,
                matched,
                row.ReceivedAtUtc,
                row.From,
                row.FromDisplayName,
                [],
                row.Snippet,
                row.HasAttachments
            );
        }

        var subjectOnly = bySubject.Where(t => !hits.ContainsKey(t.Id)).ToList();
        var latest = await LatestIncomingAsync(criteria.TenantId, subjectOnly.Select(t => t.Id).ToList(), ct);
        foreach (var row in subjectOnly)
        {
            var last = latest.GetValueOrDefault(row.Id);
            hits[row.Id] = new EmailSearchHit(
                MailSearchValues.SourceThread,
                ThreadFolder(row.Status),
                row.Id,
                null,
                row.CustomerId,
                row.Subject,
                MailSearchValues.MatchedSubject,
                row.LastMessageAtUtc,
                last?.From,
                last?.FromDisplayName,
                [],
                last?.Snippet,
                last?.HasAttachments ?? false
            );
        }

        // Adjuntos y no leídos son de la conversación entera, no solo del entrante que coincidió.
        var ids = hits.Keys.ToList();
        var withAttachments = await ThreadIdsAsync(
            MailVisibilityQueries.ThreadsWithAttachments(db, ThreadsById(ids)),
            ct
        );
        var unread = await ThreadIdsAsync(
            MailVisibilityQueries.ThreadsWithUnread(db, ThreadsById(ids), criteria.TenantId),
            ct
        );
        return hits
            .Values.Select(h =>
                h with
                {
                    HasAttachments = withAttachments.Contains(h.ThreadId!.Value),
                    IsUnread = unread.Contains(h.ThreadId!.Value),
                }
            )
            .ToList();
    }

    // Entrantes en la papelera: mismo alcance que la carpeta Trash (cliente asignado + buzón).
    private async Task<IReadOnlyList<EmailSearchHit>> SearchTrashedIncomingAsync(
        MailSearchCriteria criteria,
        CancellationToken ct
    )
    {
        var trashed = db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == criteria.TenantId && x.DeletedAtUtc != null);
        trashed = MailVisibilityQueries.IncomingOfVisibleCustomers(
            db,
            trashed,
            criteria.TenantId,
            criteria.RestrictToUserId,
            criteria.VisibleAccountIds
        );
        if (criteria.CustomerId is { } customerId)
            trashed = trashed.Where(x => x.CustomerId == customerId);
        if (criteria.UnreadOnly)
            trashed = trashed.Where(x => !x.IsRead);
        trashed = MailVisibilityQueries.FilterIncoming(
            trashed,
            new MailListFilter(criteria.Term, criteria.HasAttachmentsOnly, criteria.FromUtc, criteria.ToUtc)
        );

        var rows = await trashed.OrderByDescending(x => x.DeletedAtUtc).Take(criteria.Limit).ToListAsync(ct);

        return rows.Select(x => new EmailSearchHit(
                MailSearchValues.SourceIncoming,
                MailSearchValues.FolderTrash,
                x.EmailThreadId,
                x.Id,
                x.CustomerId,
                x.Subject,
                IncomingMatchedField(x, criteria.Term),
                x.ReceivedAtUtc,
                x.From,
                x.FromDisplayName,
                [],
                x.Snippet,
                x.HasAttachments,
                !x.IsRead
            ))
            .ToList();
    }

    // Enviados (Sent o en la papelera) y borradores abiertos, por asunto o destinatarios.
    private async Task<IReadOnlyList<EmailSearchHit>> SearchOutgoingAsync(
        MailSearchCriteria criteria,
        DraftStatus status,
        CancellationToken ct
    )
    {
        var drafts =
            status == DraftStatus.Sent ? VisibleSent(criteria, includeTrashed: true) : VisibleOpenDrafts(criteria);
        drafts = MailVisibilityQueries.FilterOutgoing(
            drafts,
            new MailListFilter(criteria.Term, criteria.HasAttachmentsOnly, criteria.FromUtc, criteria.ToUtc)
        );

        var rows = await drafts
            .Include(d => d.Recipients)
            .Include(d => d.Attachments)
            .OrderByDescending(d => d.UpdatedAtUtc)
            .Take(criteria.Limit)
            .ToListAsync(ct);

        return rows.Select(d => new EmailSearchHit(
                MailSearchValues.SourceOutgoing,
                OutgoingFolder(d),
                d.EmailThreadId,
                d.Id,
                d.CustomerId,
                d.Subject,
                ContainsIgnoreCase(d.Subject, criteria.Term)
                    ? MailSearchValues.MatchedSubject
                    : MailSearchValues.MatchedTo,
                d.UpdatedAtUtc,
                null,
                null,
                ToAddresses(d.Recipients),
                null,
                d.Attachments.Count > 0
            ))
            .ToList();
    }

    // Adjuntos de entrantes vivos (no inline) de conversaciones visibles.
    private async Task<IReadOnlyList<FileSearchHit>> SearchIncomingAttachmentsAsync(
        MailSearchCriteria criteria,
        CancellationToken ct
    )
    {
        var term = criteria.Term;
        var threads = VisibleThreads(criteria);
        if (criteria.CustomerId is { } customerId)
            threads = threads.Where(t => t.CustomerId == customerId);

        var rows = await (
            from a in db.IncomingEmailAttachments.AsNoTracking()
            join ie in db.IncomingEmails.AsNoTracking().IgnoreQueryFilters() on a.IncomingEmailId equals ie.Id
            where
                a.TenantId == criteria.TenantId
                && !a.IsInline
                && a.Filename.Contains(term)
                && ie.TenantId == criteria.TenantId
                && ie.DeletedAtUtc == null
                && (criteria.FromUtc == null || ie.ReceivedAtUtc >= criteria.FromUtc)
                && (criteria.ToUtc == null || ie.ReceivedAtUtc <= criteria.ToUtc)
                && threads.Any(t => t.Id == ie.EmailThreadId)
            orderby ie.ReceivedAtUtc descending
            select new
            {
                a.Id,
                MessageId = ie.Id,
                ie.EmailThreadId,
                ie.CustomerId,
                a.Filename,
                a.ContentType,
                a.SizeBytes,
                ie.ReceivedAtUtc,
                ie.From,
                ie.FromDisplayName,
                a.CloudStorageFileId,
                a.DownloadStatus,
            }
        )
            .Take(criteria.Limit)
            .ToListAsync(ct);

        return rows.Select(r => new FileSearchHit(
                MailSearchValues.SourceIncoming,
                r.Id,
                r.MessageId,
                r.EmailThreadId,
                r.CustomerId,
                r.Filename,
                r.ContentType,
                r.SizeBytes,
                r.ReceivedAtUtc,
                r.From,
                r.FromDisplayName,
                [],
                r.CloudStorageFileId,
                r.DownloadStatus.ToString()
            ))
            .ToList();
    }

    // Adjuntos de enviados vivos, con el mismo alcance que la carpeta Sent.
    private async Task<IReadOnlyList<FileSearchHit>> SearchOutgoingAttachmentsAsync(
        MailSearchCriteria criteria,
        CancellationToken ct
    )
    {
        var term = criteria.Term;
        var sent = MailVisibilityQueries.FilterOutgoing(
            VisibleSent(criteria, includeTrashed: false),
            new MailListFilter(null, false, criteria.FromUtc, criteria.ToUtc)
        );

        var rows = await (
            from a in db.DraftAttachments.AsNoTracking()
            join d in sent on a.DraftId equals d.Id
            where a.TenantId == criteria.TenantId && a.Filename.Contains(term)
            orderby d.UpdatedAtUtc descending
            select new
            {
                a.Id,
                a.DraftId,
                d.EmailThreadId,
                d.CustomerId,
                a.Filename,
                a.ContentType,
                a.SizeBytes,
                d.UpdatedAtUtc,
                a.FileId,
            }
        )
            .Take(criteria.Limit)
            .ToListAsync(ct);

        var draftIds = rows.Select(r => r.DraftId).Distinct().ToList();
        var recipients = await db
            .DraftRecipients.AsNoTracking()
            .Where(r => draftIds.Contains(r.DraftId) && r.Type == EmailRecipientType.To)
            .Select(r => new { r.DraftId, r.Address })
            .ToListAsync(ct);
        var toByDraft = recipients
            .GroupBy(r => r.DraftId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Address).ToList());

        return rows.Select(r => new FileSearchHit(
                MailSearchValues.SourceOutgoing,
                r.Id,
                r.DraftId,
                r.EmailThreadId,
                r.CustomerId,
                r.Filename,
                r.ContentType,
                r.SizeBytes,
                r.UpdatedAtUtc,
                null,
                null,
                toByDraft.GetValueOrDefault(r.DraftId) ?? [],
                r.FileId,
                null
            ))
            .ToList();
    }

    private IQueryable<EmailThread> VisibleThreads(MailSearchCriteria criteria) =>
        MailVisibilityQueries.VisibleThreads(
            db,
            criteria.TenantId,
            criteria.VisibleAccountIds,
            criteria.RestrictToUserId
        );

    // Mismo alcance que GET /sent?scope=all (y la papelera de enviados).
    private IQueryable<Draft> VisibleSent(MailSearchCriteria criteria, bool includeTrashed)
    {
        var drafts = db
            .Drafts.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == criteria.TenantId && d.Status == DraftStatus.Sent);
        if (!includeTrashed)
            drafts = drafts.Where(d => d.DeletedAtUtc == null);
        drafts = MailVisibilityQueries.OutgoingInScope(db, drafts, DraftCustomerScopeFor(criteria.RestrictToUserId));
        return OfRequestedCustomer(
            MailVisibilityQueries.FromVisibleMailbox(drafts, criteria.VisibleAccountIds),
            criteria
        );
    }

    // Mismo alcance que GET /drafts?scope=all: los propios, salvo quien ve el buzón de oficina.
    private IQueryable<Draft> VisibleOpenDrafts(MailSearchCriteria criteria)
    {
        var drafts = db
            .Drafts.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == criteria.TenantId && d.Status == DraftStatus.Draft);
        drafts = MailVisibilityQueries.OutgoingInScope(db, drafts, DraftCustomerScopeFor(criteria.RestrictToUserId));
        if (criteria.DraftOwnerUserId is { } owner)
            drafts = drafts.Where(d => d.CreatedByUserId == owner);
        return OfRequestedCustomer(drafts, criteria);
    }

    // Filtro opcional de cliente: uno concreto o "sin cliente".
    private static IQueryable<Draft> OfRequestedCustomer(IQueryable<Draft> drafts, MailSearchCriteria criteria)
    {
        if (criteria.CustomerId is { } customerId)
            return drafts.Where(d => d.CustomerId == customerId);
        return criteria.Unlinked ? drafts.Where(d => d.CustomerId == null) : drafts;
    }

    // Conversaciones visibles con los filtros de la búsqueda (las fechas dependen de la fuente y van aparte).
    private IQueryable<EmailThread> FilteredThreads(MailSearchCriteria criteria)
    {
        var threads = VisibleThreads(criteria);
        if (criteria.CustomerId is { } customerId)
            threads = threads.Where(t => t.CustomerId == customerId);
        if (criteria.Unlinked)
            threads = threads.Where(t => t.CustomerId == null);
        if (criteria.HasAttachmentsOnly)
            threads = MailVisibilityQueries.ThreadsWithAttachments(db, threads);
        if (criteria.UnreadOnly)
            threads = MailVisibilityQueries.ThreadsWithUnread(db, threads, criteria.TenantId);
        return threads;
    }

    private IQueryable<EmailThread> ThreadsById(IReadOnlyCollection<Guid> ids) =>
        db.EmailThreads.AsNoTracking().IgnoreQueryFilters().Where(t => ids.Contains(t.Id));

    private static async Task<HashSet<Guid>> ThreadIdsAsync(IQueryable<EmailThread> threads, CancellationToken ct) =>
        (await threads.Select(t => t.Id).ToListAsync(ct)).ToHashSet();

    private async Task<Dictionary<Guid, IncomingEmail>> LatestIncomingAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> threadIds,
        CancellationToken ct
    )
    {
        if (threadIds.Count == 0)
            return [];

        var latest = await db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.DeletedAtUtc == null && threadIds.Contains(x.EmailThreadId))
            .GroupBy(x => x.EmailThreadId)
            .Select(g => g.OrderByDescending(x => x.ReceivedAtUtc).First())
            .ToListAsync(ct);
        return latest.ToDictionary(x => x.EmailThreadId);
    }

    private static Application.Abstractions.DraftCustomerScope DraftCustomerScopeFor(Guid? restrictToUserId) =>
        Application.Abstractions.DraftCustomerScope.All.RestrictedTo(restrictToUserId);

    private static string ThreadFolder(EmailThreadStatus status) =>
        status == EmailThreadStatus.Archived ? MailSearchValues.FolderArchived : MailSearchValues.FolderInbox;

    private static string OutgoingFolder(Draft draft) =>
        draft.Status == DraftStatus.Draft ? MailSearchValues.FolderDrafts
        : draft.DeletedAtUtc is not null ? MailSearchValues.FolderTrash
        : MailSearchValues.FolderSent;

    private static string IncomingMatchedField(IncomingEmail email, string term) =>
        ContainsIgnoreCase(email.Subject, term) ? MailSearchValues.MatchedSubject
        : ContainsIgnoreCase(email.From, term) || ContainsIgnoreCase(email.FromDisplayName, term)
            ? MailSearchValues.MatchedFrom
        : MailSearchValues.MatchedSnippet;

    private static IReadOnlyList<string> ToAddresses(IEnumerable<DraftRecipient> recipients) =>
        recipients.Where(r => r.Type == EmailRecipientType.To).Select(r => r.Address).ToList();

    private static bool ContainsIgnoreCase(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
