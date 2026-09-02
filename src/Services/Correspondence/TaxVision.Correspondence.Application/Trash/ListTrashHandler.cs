using BuildingBlocks.Common;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Trash;

// Papelera del customer: une entrantes y enviados borrados, más reciente primero.
// Merge en memoria (la papelera de un customer está acotada); paginación aproximada en el borde.
public static class ListTrashHandler
{
    public static async Task<PagedResult<TrashItem>> Handle(
        ListTrashQuery query,
        IIncomingEmailRepository incomingEmails,
        IDraftRepository drafts,
        CancellationToken ct
    )
    {
        var take = query.Page * query.Size;
        var incoming = await incomingEmails.ListTrashedByCustomerAsync(query.TenantId, query.CustomerId, 1, take, ct);
        var sent = await drafts.ListTrashedSentByCustomerAsync(query.TenantId, query.CustomerId, 1, take, ct);

        var merged = incoming
            .Items.Select(ToItem)
            .Concat(sent.Items.Select(ToItem))
            .OrderByDescending(x => x.DeletedAtUtc)
            .ToList();

        var pageItems = merged.Skip((query.Page - 1) * query.Size).Take(query.Size).ToList();
        return new PagedResult<TrashItem>(pageItems, query.Page, query.Size, incoming.TotalCount + sent.TotalCount);
    }

    private static TrashItem ToItem(IncomingEmail email) =>
        new(
            email.Id,
            "Incoming",
            email.EmailThreadId,
            email.Subject,
            email.From,
            email.DeletedAtUtc!.Value,
            email.HasAttachments,
            email.AttachmentCount
        );

    private static TrashItem ToItem(Draft draft) =>
        new(
            draft.Id,
            "Sent",
            draft.EmailThreadId,
            draft.Subject,
            draft.Recipients.Where(r => r.Type == EmailRecipientType.To).Select(r => r.Address).FirstOrDefault() ?? "",
            draft.DeletedAtUtc!.Value,
            draft.Attachments.Count > 0,
            draft.Attachments.Count
        );
}
