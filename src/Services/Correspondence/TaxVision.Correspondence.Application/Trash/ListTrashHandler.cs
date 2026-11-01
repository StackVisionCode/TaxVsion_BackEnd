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
        var incoming = await ListTrashedIncomingAsync(query, incomingEmails, take, ct);
        var sent = await drafts.ListTrashedSentByCustomerAsync(
            query.TenantId,
            query.Customer,
            1,
            take,
            query.VisibleAccountIds,
            ct
        );

        var merged = incoming
            .Items.Select(ToItem)
            .Concat(sent.Items.Select(ToItem))
            .OrderByDescending(x => x.DeletedAtUtc)
            .ToList();

        var pageItems = merged.Skip((query.Page - 1) * query.Size).Take(query.Size).ToList();
        return new PagedResult<TrashItem>(pageItems, query.Page, query.Size, incoming.TotalCount + sent.TotalCount);
    }

    // Solo entra correo de clientes: sin cliente no hay entrantes, solo enviados.
    private static Task<PagedResult<IncomingEmail>> ListTrashedIncomingAsync(
        ListTrashQuery query,
        IIncomingEmailRepository incomingEmails,
        int take,
        CancellationToken ct
    ) =>
        query.Customer.IsUnlinked
            ? Task.FromResult(new PagedResult<IncomingEmail>([], 1, take, 0))
            : incomingEmails.ListTrashedByCustomerAsync(
                query.TenantId,
                query.Customer.CustomerId,
                query.Customer.RestrictToUserId,
                1,
                take,
                query.VisibleAccountIds,
                ct
            );

    private static TrashItem ToItem(IncomingEmail email) =>
        new(
            email.Id,
            "Incoming",
            email.EmailThreadId,
            email.Subject,
            email.From,
            email.DeletedAtUtc!.Value,
            email.HasAttachments,
            email.AttachmentCount,
            email.CustomerId
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
            draft.Attachments.Count,
            draft.CustomerId
        );
}
