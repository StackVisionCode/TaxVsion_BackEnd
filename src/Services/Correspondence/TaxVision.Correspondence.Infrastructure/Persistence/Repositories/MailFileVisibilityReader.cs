using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Contracts.Work;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

/// <summary>
/// Un adjunto es visible si lo lleva un enviado vivo que pasa la regla de Sent o está en un hilo visible, o un
/// entrante vivo ya descargado de un hilo visible (<see cref="MailVisibilityQueries"/>). Por archivo gana la
/// referencia descargable más reciente.
/// </summary>
public sealed class MailFileVisibilityReader(CorrespondenceDbContext db) : IMailFileVisibilityReader
{
    private sealed record Reference(
        Guid FileId,
        Guid MessageId,
        Guid? ThreadId,
        string Subject,
        Guid? CustomerId,
        Guid AccountId,
        DateTime At,
        bool Incoming
    );

    public async Task<IReadOnlyList<VisibleFile>> GetVisibleFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        Guid viewerUserId,
        bool canDownloadIncoming,
        CancellationToken ct = default
    )
    {
        var ids = MailVisibilityQueries.AsArray(fileIds);
        var visibleThreads = MailVisibilityQueries
            .VisibleThreads(db, tenantId, visibleAccountIds, restrictToUserId)
            .Select(t => t.Id);
        var references = await SentReferencesAsync(
            tenantId,
            ids,
            visibleAccountIds,
            restrictToUserId,
            visibleThreads,
            ct
        );
        references.AddRange(await IncomingReferencesAsync(tenantId, ids, visibleThreads, ct));

        var chosen = references
            .GroupBy(r => r.FileId)
            .Select(g =>
                g.OrderByDescending(r => !r.Incoming || canDownloadIncoming)
                    .ThenByDescending(r => r.At)
                    .ThenByDescending(r => r.MessageId)
                    .First()
            )
            .ToList();
        var personal = await PersonalMessagesAsync(tenantId, viewerUserId, chosen, ct);

        return chosen
            .Select(r => new VisibleFile(
                r.FileId,
                r.ThreadId is null ? WorkResourceTypes.EmailMessage : WorkResourceTypes.EmailThread,
                r.ThreadId ?? r.MessageId,
                WorkLimits.ClipTitle(r.Subject),
                r.CustomerId,
                new Dictionary<string, string>
                {
                    ["messageId"] = r.MessageId.ToString(),
                    ["accountId"] = r.AccountId.ToString(),
                },
                CanDownload: !r.Incoming || canDownloadIncoming,
                RestrictedTo: personal.Contains(r.MessageId) ? FileVisibilityRestrictions.PersonalMailbox : null
            ))
            .ToList();
    }

    // Enviados: regla de Sent (GET /sent?scope=all) o conversación visible (un hilo mixto se muestra entero).
    private async Task<List<Reference>> SentReferencesAsync(
        Guid tenantId,
        Guid[] ids,
        IReadOnlyCollection<Guid>? visibleAccountIds,
        Guid? restrictToUserId,
        IQueryable<Guid> visibleThreads,
        CancellationToken ct
    )
    {
        var sent = db
            .Drafts.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.Status == DraftStatus.Sent && d.DeletedAtUtc == null);
        var inSent = MailVisibilityQueries
            .FromVisibleMailbox(
                MailVisibilityQueries.OutgoingInScope(db, sent, DraftCustomerScope.All.RestrictedTo(restrictToUserId)),
                visibleAccountIds
            )
            .Select(d => d.Id);
        var visibleSent = sent.Where(d =>
            inSent.Contains(d.Id) || (d.EmailThreadId != null && visibleThreads.Contains(d.EmailThreadId.Value))
        );

        return await db
            .DraftAttachments.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && ids.Contains(a.FileId))
            .Join(
                visibleSent,
                a => a.DraftId,
                d => d.Id,
                (a, d) =>
                    new Reference(
                        a.FileId,
                        d.Id,
                        d.EmailThreadId,
                        d.Subject,
                        d.CustomerId,
                        d.AccountId,
                        d.UpdatedAtUtc,
                        false
                    )
            )
            .ToListAsync(ct);
    }

    // Entrantes: solo lo ya descargado (lo no abierto no existe en CloudStorage), fuera de la papelera, hilo visible.
    private async Task<List<Reference>> IncomingReferencesAsync(
        Guid tenantId,
        Guid[] ids,
        IQueryable<Guid> visibleThreads,
        CancellationToken ct
    )
    {
        var emails = db
            .IncomingEmails.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.DeletedAtUtc == null && visibleThreads.Contains(e.EmailThreadId));

        return await db
            .IncomingEmailAttachments.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a =>
                a.TenantId == tenantId
                && a.CloudStorageFileId != null
                && ids.Contains(a.CloudStorageFileId.Value)
                && a.DownloadStatus == AttachmentDownloadStatus.Downloaded
            )
            .Join(
                emails,
                a => a.IncomingEmailId,
                e => e.Id,
                (a, e) =>
                    new Reference(
                        a.CloudStorageFileId!.Value,
                        e.Id,
                        e.EmailThreadId,
                        e.Subject,
                        e.CustomerId,
                        e.AccountId,
                        e.ReceivedAtUtc,
                        true
                    )
            )
            .ToListAsync(ct);
    }

    /// <summary>
    /// Mensajes elegidos que viven solo en buzones personales de quien mira (proyección <c>MailboxOwnership</c>).
    /// Buzón de oficina, de otro empleado o sin proyección: sin marca.
    /// </summary>
    private async Task<HashSet<Guid>> PersonalMessagesAsync(
        Guid tenantId,
        Guid viewerUserId,
        IReadOnlyList<Reference> chosen,
        CancellationToken ct
    )
    {
        if (chosen.Count == 0 || viewerUserId == Guid.Empty)
            return [];

        var accountsByMessage = chosen
            .GroupBy(r => r.MessageId)
            .ToDictionary(g => g.Key, g => new HashSet<Guid>(g.Select(r => r.AccountId)));
        var incomingIds = chosen.Where(r => r.Incoming).Select(r => r.MessageId).Distinct().ToArray();
        if (incomingIds.Length > 0)
        {
            var copies = await db
                .IncomingEmailMailboxes.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && incomingIds.Contains(m.IncomingEmailId))
                .Select(m => new { m.IncomingEmailId, m.AccountId })
                .ToListAsync(ct);
            foreach (var copy in copies)
                accountsByMessage[copy.IncomingEmailId].Add(copy.AccountId);
        }

        var accounts = accountsByMessage.Values.SelectMany(a => a).Distinct().ToArray();
        var mine = (
            await db
                .MailboxOwnerships.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId && accounts.Contains(o.AccountId) && o.OwnerUserId == viewerUserId)
                .Select(o => o.AccountId)
                .ToListAsync(ct)
        ).ToHashSet();

        return accountsByMessage.Where(kv => kv.Value.All(mine.Contains)).Select(kv => kv.Key).ToHashSet();
    }
}
