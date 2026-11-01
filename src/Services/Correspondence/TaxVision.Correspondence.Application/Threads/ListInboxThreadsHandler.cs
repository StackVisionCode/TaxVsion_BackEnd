using BuildingBlocks.Common;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Threads;

public sealed record ListInboxThreadsQuery(InboxThreadFilter Filter);

/// <summary>
/// Inbox de la oficina: hilos de todos los clientes visibles (buzón + cliente asignado), más reciente primero.
/// No leídos, último remitente y adjuntos se calculan solo para la página (consultas agregadas, no N).
/// </summary>
public static class ListInboxThreadsHandler
{
    public static async Task<PagedResult<InboxThreadSummary>> Handle(
        ListInboxThreadsQuery query,
        IEmailThreadRepository emailThreads,
        IIncomingEmailRepository incomingEmails,
        CancellationToken ct
    )
    {
        var filter = query.Filter;
        var page = await emailThreads.ListVisibleAsync(filter, ct);
        var threadIds = page.Items.Select(thread => thread.Id).ToList();

        var unread = await incomingEmails.GetUnreadCountsByThreadAsync(filter.TenantId, threadIds, ct);
        var latest = await incomingEmails.GetLatestByThreadAsync(filter.TenantId, threadIds, ct);
        var withAttachments = await incomingEmails.GetThreadIdsWithAttachmentsAsync(filter.TenantId, threadIds, ct);

        var items = page
            .Items.Select(thread =>
                ToSummary(
                    thread,
                    unread.GetValueOrDefault(thread.Id, 0),
                    latest.GetValueOrDefault(thread.Id),
                    withAttachments.Contains(thread.Id)
                )
            )
            .ToList();
        return new PagedResult<InboxThreadSummary>(items, page.Page, page.Size, page.TotalCount);
    }

    private static InboxThreadSummary ToSummary(
        EmailThread thread,
        int unreadCount,
        IncomingEmail? latest,
        bool hasAttachments
    ) =>
        new(
            thread.Id,
            thread.CustomerId,
            thread.Subject,
            thread.Status.ToString(),
            thread.MessageCount,
            thread.FirstMessageAtUtc,
            thread.LastMessageAtUtc,
            unreadCount,
            latest?.From,
            latest?.FromDisplayName,
            latest?.Snippet,
            hasAttachments
        );
}
