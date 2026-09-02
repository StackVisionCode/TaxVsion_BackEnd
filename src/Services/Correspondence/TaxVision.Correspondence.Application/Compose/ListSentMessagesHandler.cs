using BuildingBlocks.Common;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Carpeta "Sent" del cliente final (<c>GET /correspondence/sent</c>) — mensajes ya enviados de un
/// customer, más reciente primero. HTTP-triggered, no un consumer Wolverine (no empuja correlación),
/// mismo criterio que <see cref="ListDraftsHandler"/>. Filtro puro (tenant + customer): un customer
/// sin enviados no es un error, es una página vacía.
/// </summary>
public static class ListSentMessagesHandler
{
    public static async Task<PagedResult<SentMessageListItem>> Handle(
        ListSentMessagesQuery query,
        IDraftRepository drafts,
        CancellationToken ct
    )
    {
        var page = await drafts.ListSentByCustomerAsync(query.TenantId, query.CustomerId, query.Page, query.Size, ct);

        var items = page.Items.Select(ToListItem).ToList();
        return new PagedResult<SentMessageListItem>(items, page.Page, page.Size, page.TotalCount);
    }

    private static SentMessageListItem ToListItem(Draft draft) =>
        new(
            draft.Id,
            draft.EmailThreadId,
            draft.Subject,
            draft.Recipients.Where(r => r.Type == EmailRecipientType.To).Select(r => r.Address).ToList(),
            draft.ReplyContext is not null,
            // UpdatedAtUtc ES el instante de envío una vez Sent — ningún método del aggregate lo vuelve
            // a tocar después (mismo criterio que ListThreadMessagesHandler.ToOutboundSummary).
            draft.UpdatedAtUtc,
            draft.Attachments.Count > 0,
            draft.Attachments.Count
        );
}
