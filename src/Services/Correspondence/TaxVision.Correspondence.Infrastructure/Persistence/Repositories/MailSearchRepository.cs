using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Search;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

/// <summary>
/// Búsqueda unificada sobre las mismas reglas de <see cref="MailVisibilityQueries"/>: cada fuente se acota por
/// tenant y por <see cref="MailSearchCriteria.Limit"/> en SQL; la mezcla y el corte final se hacen en memoria
/// (como mucho cuatro veces el límite). Los filtros (texto por campo, carpeta, cliente, sin cliente, adjuntos, no
/// leídos, fechas) también van en SQL: filtrar en memoria después del límite devolvería resultados incompletos.
/// Personas se agrega en SQL (GROUP BY dirección sobre la unión de las fuentes) y solo se traen las del límite.
/// </summary>
public sealed class MailSearchRepository(CorrespondenceDbContext db) : IMailSearchRepository
{
    public async Task<IReadOnlyList<EmailSearchHit>> SearchEmailsAsync(
        MailSearchCriteria criteria,
        CancellationToken ct = default
    )
    {
        var hits = new List<EmailSearchHit>();
        if (InConversations(criteria))
            hits.AddRange(await SearchConversationsAsync(criteria, ct));
        // Un entrante siempre es de un cliente: "sin cliente" no tiene entrantes.
        if (!criteria.Unlinked && criteria.Includes(MailSearchFolder.Trash))
            hits.AddRange(await SearchTrashedIncomingAsync(criteria, ct));
        // Lo saliente nunca está "sin leer".
        if (!criteria.UnreadOnly)
        {
            if (criteria.Includes(MailSearchFolder.Sent) || criteria.Includes(MailSearchFolder.Trash))
                hits.AddRange(await SearchOutgoingAsync(criteria, DraftStatus.Sent, ct));
            if (criteria.Includes(MailSearchFolder.Drafts))
                hits.AddRange(await SearchOutgoingAsync(criteria, DraftStatus.Draft, ct));
        }
        return hits.OrderByDescending(h => h.OccurredAtUtc).Take(criteria.Limit).ToList();
    }

    public async Task<IReadOnlyList<FileSearchHit>> SearchFilesAsync(
        MailSearchCriteria criteria,
        CancellationToken ct = default
    )
    {
        // Archivos = adjuntos de correo vivo: entrantes de Inbox/Archived y enviados de Sent.
        var hits = new List<FileSearchHit>();
        if (!criteria.Unlinked && InConversations(criteria))
            hits.AddRange(await SearchIncomingAttachmentsAsync(criteria, ct));
        if (criteria.Includes(MailSearchFolder.Sent))
            hits.AddRange(await SearchOutgoingAttachmentsAsync(criteria, ct));
        return hits.OrderByDescending(h => h.OccurredAtUtc).Take(criteria.Limit).ToList();
    }

    public async Task<IReadOnlyList<PersonSearchHit>> SearchPeopleAsync(
        MailSearchCriteria criteria,
        CancellationToken ct = default
    )
    {
        var sources = PeopleSources(criteria).ToList();
        if (sources.Count == 0)
            return [];
        var rows = sources.Aggregate((all, next) => all.Concat(next));

        var top = await rows.GroupBy(r => r.Address)
            .Select(g => new
            {
                Address = g.Key,
                Count = g.Count(),
                Last = g.Max(r => r.At),
                Incoming = g.Sum(r => r.Incoming),
                Outgoing = g.Sum(r => r.Outgoing),
            })
            .OrderByDescending(p => p.Last)
            .ThenBy(p => p.Address)
            .Take(criteria.Limit)
            .ToListAsync(ct);
        if (top.Count == 0)
            return [];

        // Nombre y cliente más recientes no nulos de las direcciones del límite, con ROW_NUMBER por dirección (una
        // subconsulta correlacionada por persona repetía la unión con toda la visibilidad: medido, 10 s).
        var addresses = top.Select(p => p.Address).ToArray();
        var ofTop = rows.Where(r => addresses.Contains(r.Address));
        var names = await LatestPerAddressAsync(ofTop.Where(r => r.DisplayName != null), ct);
        var customers = await LatestPerAddressAsync(ofTop.Where(r => r.CustomerId != null), ct);

        return top.Select(p => new PersonSearchHit(
                p.Address,
                names.GetValueOrDefault(p.Address)?.DisplayName,
                customers.GetValueOrDefault(p.Address)?.CustomerId,
                p.Count,
                p.Last,
                p.Incoming > 0 && p.Outgoing > 0 ? MailSearchValues.DirectionBoth
                    : p.Incoming > 0 ? MailSearchValues.DirectionFrom
                    : MailSearchValues.DirectionTo
            ))
            .ToList();
    }

    private static async Task<Dictionary<string, PersonRow>> LatestPerAddressAsync(
        IQueryable<PersonRow> rows,
        CancellationToken ct
    ) =>
        (
            await rows.GroupBy(r => r.Address).Select(g => g.OrderByDescending(r => r.At).First()).ToListAsync(ct)
        ).ToDictionary(r => r.Address);

    // Inbox/Archived: una fila por conversación, por su asunto o por un entrante vivo que coincide.
    private async Task<IReadOnlyList<EmailSearchHit>> SearchConversationsAsync(
        MailSearchCriteria criteria,
        CancellationToken ct
    )
    {
        var term = criteria.Term;
        var threads = FilteredThreads(criteria);
        if (criteria.Subject is { } subject)
            threads = threads.Where(t => t.Subject.Contains(subject));
        // from/to son del mensaje: con ellos la conversación sale solo por un entrante que coincide.
        var byMessage = criteria.From is not null || criteria.To is not null;

        var byIncoming =
            term is null && !byMessage ? [] : await ConversationsByIncomingAsync(criteria, threads, byMessage, ct);

        var bySubject =
            byMessage || (term is null && criteria.Subject is null)
                ? []
                : await ConversationsBySubjectAsync(criteria, threads, ct);

        var hits = new Dictionary<Guid, EmailSearchHit>();
        foreach (var hit in byIncoming)
            hits.TryAdd(hit.ThreadId!.Value, hit);

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

    // Entrantes vivos de las conversaciones filtradas; con from/to, q también vale sobre el asunto de la conversación.
    private async Task<IReadOnlyList<EmailSearchHit>> ConversationsByIncomingAsync(
        MailSearchCriteria criteria,
        IQueryable<EmailThread> threads,
        bool byMessage,
        CancellationToken ct
    )
    {
        var term = criteria.Term;
        var incoming = IncomingFields(LiveIncoming(criteria), criteria, withSubject: false);
        var joined = from ie in incoming join t in threads on ie.EmailThreadId equals t.Id select new { ie, t };
        if (term is not null)
            joined = byMessage
                ? joined.Where(x =>
                    x.ie.From.Contains(term)
                    || (x.ie.FromDisplayName != null && x.ie.FromDisplayName.Contains(term))
                    || x.ie.Snippet.Contains(term)
                    || x.t.Subject.Contains(term)
                )
                : joined.Where(x =>
                    x.ie.From.Contains(term)
                    || (x.ie.FromDisplayName != null && x.ie.FromDisplayName.Contains(term))
                    || x.ie.Snippet.Contains(term)
                );

        var rows = await joined
            .OrderByDescending(x => x.ie.ReceivedAtUtc)
            .Select(x => new
            {
                x.ie.Id,
                ThreadId = x.t.Id,
                x.t.CustomerId,
                x.t.Subject,
                x.t.Status,
                x.ie.From,
                x.ie.FromDisplayName,
                x.ie.Snippet,
                x.ie.ReceivedAtUtc,
                x.ie.HasAttachments,
            })
            .Take(criteria.Limit)
            .ToListAsync(ct);

        return rows.Select(row => new EmailSearchHit(
                MailSearchValues.SourceThread,
                ThreadFolder(row.Status),
                row.ThreadId,
                row.Id,
                row.CustomerId,
                row.Subject,
                term is null ? FieldMatched(criteria)
                    : ContainsIgnoreCase(row.From, term) || ContainsIgnoreCase(row.FromDisplayName, term)
                        ? MailSearchValues.MatchedFrom
                    : ContainsIgnoreCase(row.Snippet, term) ? MailSearchValues.MatchedSnippet
                    : MailSearchValues.MatchedSubject,
                row.ReceivedAtUtc,
                row.From,
                row.FromDisplayName,
                [],
                row.Snippet,
                row.HasAttachments
            ))
            .ToList();
    }

    private async Task<IReadOnlyList<SubjectRow>> ConversationsBySubjectAsync(
        MailSearchCriteria criteria,
        IQueryable<EmailThread> threads,
        CancellationToken ct
    )
    {
        var matching = MailVisibilityQueries.ThreadsInRange(threads, criteria.FromUtc, criteria.ToUtc);
        if (criteria.Term is { } term)
            matching = matching.Where(t => t.Subject.Contains(term));
        return await matching
            .OrderByDescending(t => t.LastMessageAtUtc)
            .Take(criteria.Limit)
            .Select(t => new SubjectRow(t.Id, t.CustomerId, t.Subject, t.Status, t.LastMessageAtUtc))
            .ToListAsync(ct);
    }

    // Entrantes en la papelera: mismo alcance que la carpeta Trash (cliente asignado + buzón).
    private async Task<IReadOnlyList<EmailSearchHit>> SearchTrashedIncomingAsync(
        MailSearchCriteria criteria,
        CancellationToken ct
    )
    {
        var trashed = TrashedIncoming(criteria);
        if (criteria.Term is { } term)
            trashed = MailVisibilityQueries.MatchingIncoming(trashed, term);

        var rows = await trashed.OrderByDescending(x => x.DeletedAtUtc).Take(criteria.Limit).ToListAsync(ct);

        return rows.Select(x => new EmailSearchHit(
                MailSearchValues.SourceIncoming,
                MailSearchValues.FolderTrash,
                x.EmailThreadId,
                x.Id,
                x.CustomerId,
                x.Subject,
                criteria.Term is { } t ? IncomingMatchedField(x, t) : FieldMatched(criteria),
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

    // Enviados (Sent o en la papelera, según la carpeta) y borradores abiertos, por asunto o destinatarios.
    private async Task<IReadOnlyList<EmailSearchHit>> SearchOutgoingAsync(
        MailSearchCriteria criteria,
        DraftStatus status,
        CancellationToken ct
    )
    {
        var drafts = OutgoingFields(OutgoingOfStatus(criteria, status), criteria, withRecipient: true);
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
                criteria.Term is not { } term ? FieldMatched(criteria)
                    : ContainsIgnoreCase(d.Subject, term) ? MailSearchValues.MatchedSubject
                    : MailSearchValues.MatchedTo,
                d.UpdatedAtUtc,
                null,
                null,
                d.AddressesOf(EmailRecipientType.To),
                null,
                d.Attachments.Count > 0,
                CcAddresses: d.AddressesOf(EmailRecipientType.Cc),
                BccAddresses: d.AddressesOf(EmailRecipientType.Bcc)
            ))
            .ToList();
    }

    // Adjuntos de entrantes vivos (no inline) de conversaciones visibles.
    private async Task<IReadOnlyList<FileSearchHit>> SearchIncomingAttachmentsAsync(
        MailSearchCriteria criteria,
        CancellationToken ct
    )
    {
        var threads = ThreadsInFolder(VisibleThreads(criteria), criteria);
        if (criteria.CustomerId is { } customerId)
            threads = threads.Where(t => t.CustomerId == customerId);
        var incoming = IncomingFields(LiveIncoming(criteria), criteria, withSubject: true)
            .Where(ie => threads.Any(t => t.Id == ie.EmailThreadId));
        var attachments = db
            .IncomingEmailAttachments.AsNoTracking()
            .Where(a => a.TenantId == criteria.TenantId && !a.IsInline);
        if (criteria.Term is { } term)
            attachments = attachments.Where(a => a.Filename.Contains(term));

        var rows = await (
            from a in attachments
            join ie in incoming on a.IncomingEmailId equals ie.Id
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
        var sent = OutgoingFields(
            MailVisibilityQueries.FilterOutgoing(
                VisibleSent(criteria, includeTrashed: false),
                new MailListFilter(null, false, criteria.FromUtc, criteria.ToUtc)
            ),
            criteria,
            withRecipient: true
        );
        var attachments = db.DraftAttachments.AsNoTracking().Where(a => a.TenantId == criteria.TenantId);
        if (criteria.Term is { } term)
            attachments = attachments.Where(a => a.Filename.Contains(term));

        var rows = await (
            from a in attachments
            join d in sent on a.DraftId equals d.Id
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

    /// <summary>
    /// Fuentes de personas, mismas reglas que los correos: remitentes de entrantes (conversaciones de Inbox/Archived y
    /// papelera) y destinatarios de enviados (Sent y papelera); los de borradores solo con <c>folder=Drafts</c> (todavía
    /// no hubo correo). <c>q</c> y <c>from</c> (entrantes) o <c>to</c> (salientes) se aplican a la persona misma; el
    /// resto de los filtros, al correo. Nunca salen los buzones del tenant.
    /// </summary>
    private IEnumerable<IQueryable<PersonRow>> PeopleSources(MailSearchCriteria criteria)
    {
        var own = (criteria.OwnMailboxAddresses ?? []).ToArray();

        if (InConversations(criteria))
        {
            var threads = FilteredThreads(criteria);
            yield return from ie in Senders(LiveIncoming(criteria), criteria, own)
            join t in threads on ie.EmailThreadId equals t.Id
            select new PersonRow
            {
                Address = ie.From,
                DisplayName = ie.FromDisplayName,
                CustomerId = t.CustomerId,
                At = ie.ReceivedAtUtc,
                Incoming = 1,
                Outgoing = 0,
            };
        }

        if (!criteria.Unlinked && criteria.Includes(MailSearchFolder.Trash))
            yield return Senders(TrashedIncoming(criteria), criteria, own)
                .Select(x => new PersonRow
                {
                    Address = x.From,
                    DisplayName = x.FromDisplayName,
                    CustomerId = x.CustomerId,
                    At = x.ReceivedAtUtc,
                    Incoming = 1,
                    Outgoing = 0,
                });

        if (criteria.UnreadOnly)
            yield break;
        if (criteria.Includes(MailSearchFolder.Sent) || criteria.Includes(MailSearchFolder.Trash))
            yield return Recipients(OutgoingOfStatus(criteria, DraftStatus.Sent), criteria, own);
        if (criteria.Folder == MailSearchFolder.Drafts)
            yield return Recipients(OutgoingOfStatus(criteria, DraftStatus.Draft), criteria, own);
    }

    // Remitentes: la persona es el From del entrante.
    private static IQueryable<IncomingEmail> Senders(
        IQueryable<IncomingEmail> emails,
        MailSearchCriteria criteria,
        string[] own
    )
    {
        emails = IncomingFields(emails, criteria, withSubject: true);
        if (criteria.Term is { } term)
            emails = emails.Where(x =>
                x.From.Contains(term) || (x.FromDisplayName != null && x.FromDisplayName.Contains(term))
            );
        return own.Length == 0 ? emails : emails.Where(x => !own.Contains(x.From));
    }

    // Destinatarios (To/Cc/Bcc): la persona es cada destinatario del saliente.
    private IQueryable<PersonRow> Recipients(IQueryable<Draft> drafts, MailSearchCriteria criteria, string[] own)
    {
        drafts = MailVisibilityQueries.FilterOutgoing(
            OutgoingFields(drafts, criteria, withRecipient: false),
            new MailListFilter(null, criteria.HasAttachmentsOnly, criteria.FromUtc, criteria.ToUtc)
        );
        var recipients = db.DraftRecipients.AsNoTracking().Where(r => r.TenantId == criteria.TenantId);
        if (criteria.To is { } to)
            recipients = recipients.Where(r =>
                r.Address.Contains(to) || (r.DisplayName != null && r.DisplayName.Contains(to))
            );
        if (criteria.Term is { } term)
            recipients = recipients.Where(r =>
                r.Address.Contains(term) || (r.DisplayName != null && r.DisplayName.Contains(term))
            );
        if (own.Length > 0)
            recipients = recipients.Where(r => !own.Contains(r.Address));

        return from r in recipients
            join d in drafts on r.DraftId equals d.Id
            select new PersonRow
            {
                Address = r.Address,
                DisplayName = r.DisplayName,
                CustomerId = d.CustomerId,
                At = d.UpdatedAtUtc,
                Incoming = 0,
                Outgoing = 1,
            };
    }

    // from/to/subject sobre un entrante: remitente (dirección o nombre), destinatarios guardados y su propio asunto.
    private static IQueryable<IncomingEmail> IncomingFields(
        IQueryable<IncomingEmail> emails,
        MailSearchCriteria criteria,
        bool withSubject
    )
    {
        if (criteria.From is { } from)
            emails = emails.Where(x =>
                x.From.Contains(from) || (x.FromDisplayName != null && x.FromDisplayName.Contains(from))
            );
        if (criteria.To is { } to)
            emails = emails.Where(x =>
                x.Recipients.Any(r => r.Address.Contains(to) || (r.DisplayName != null && r.DisplayName.Contains(to)))
            );
        if (withSubject && criteria.Subject is { } subject)
            emails = emails.Where(x => x.Subject.Contains(subject));
        return emails;
    }

    // from/to/subject sobre un saliente: buzón que envía, algún destinatario (dirección o nombre) y asunto.
    private static IQueryable<Draft> OutgoingFields(
        IQueryable<Draft> drafts,
        MailSearchCriteria criteria,
        bool withRecipient
    )
    {
        if (criteria.From is not null)
        {
            var senders = MailVisibilityQueries.AsArray(criteria.SenderAccountIds ?? []);
            drafts = drafts.Where(d => senders.Contains(d.AccountId));
        }
        if (withRecipient && criteria.To is { } to)
            drafts = drafts.Where(d =>
                d.Recipients.Any(r => r.Address.Contains(to) || (r.DisplayName != null && r.DisplayName.Contains(to)))
            );
        if (criteria.Subject is { } subject)
            drafts = drafts.Where(d => d.Subject.Contains(subject));
        return drafts;
    }

    // Entrantes vivos del tenant en el rango (fecha = recepción).
    private IQueryable<IncomingEmail> LiveIncoming(MailSearchCriteria criteria)
    {
        var emails = db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(ie => ie.TenantId == criteria.TenantId && ie.DeletedAtUtc == null);
        if (criteria.FromUtc is { } from)
            emails = emails.Where(ie => ie.ReceivedAtUtc >= from);
        if (criteria.ToUtc is { } to)
            emails = emails.Where(ie => ie.ReceivedAtUtc <= to);
        return emails;
    }

    // Entrantes de la papelera con el alcance de la carpeta Trash y los filtros de la búsqueda (sin texto).
    private IQueryable<IncomingEmail> TrashedIncoming(MailSearchCriteria criteria)
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
            new MailListFilter(null, criteria.HasAttachmentsOnly, criteria.FromUtc, criteria.ToUtc)
        );
        return IncomingFields(trashed, criteria, withSubject: true);
    }

    // Enviados de la carpeta pedida (Sent = vivos, Trash = en la papelera, ninguna = ambos) o borradores abiertos.
    private IQueryable<Draft> OutgoingOfStatus(MailSearchCriteria criteria, DraftStatus status)
    {
        if (status == DraftStatus.Draft)
            return VisibleOpenDrafts(criteria);
        var sent = VisibleSent(criteria, includeTrashed: true);
        if (!criteria.Includes(MailSearchFolder.Sent))
            return sent.Where(d => d.DeletedAtUtc != null);
        return criteria.Includes(MailSearchFolder.Trash) ? sent : sent.Where(d => d.DeletedAtUtc == null);
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
        var threads = ThreadsInFolder(VisibleThreads(criteria), criteria);
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

    private static IQueryable<EmailThread> ThreadsInFolder(
        IQueryable<EmailThread> threads,
        MailSearchCriteria criteria
    ) =>
        criteria.Folder switch
        {
            MailSearchFolder.Inbox => threads.Where(t => t.Status == EmailThreadStatus.Active),
            MailSearchFolder.Archived => threads.Where(t => t.Status == EmailThreadStatus.Archived),
            _ => threads,
        };

    private static bool InConversations(MailSearchCriteria criteria) =>
        criteria.Folder is null or MailSearchFolder.Inbox or MailSearchFolder.Archived;

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

    // Sin q: el primer campo que vino (from, to, subject).
    private static string FieldMatched(MailSearchCriteria criteria) =>
        criteria.From is not null ? MailSearchValues.MatchedFrom
        : criteria.To is not null ? MailSearchValues.MatchedTo
        : MailSearchValues.MatchedSubject;

    private static bool ContainsIgnoreCase(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private sealed record SubjectRow(
        Guid Id,
        Guid? CustomerId,
        string Subject,
        EmailThreadStatus Status,
        DateTime LastMessageAtUtc
    );

    // Una aparición de una persona en un correo (fila de la unión que se agrupa por dirección).
    private sealed class PersonRow
    {
        public string Address { get; init; } = default!;
        public string? DisplayName { get; init; }
        public Guid? CustomerId { get; init; }
        public DateTime At { get; init; }
        public int Incoming { get; init; }
        public int Outgoing { get; init; }
    }
}
