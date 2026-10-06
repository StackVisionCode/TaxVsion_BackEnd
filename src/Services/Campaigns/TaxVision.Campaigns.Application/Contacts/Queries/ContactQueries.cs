using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Application.Contacts.Queries;

// ─────────────────────────── Contacts ───────────────────────────

public sealed record ListContactsQuery(Guid TenantId, int Page, int Size);

public static class ListContactsHandler
{
    public static async Task<PagedResult<ContactResponse>> Handle(
        ListContactsQuery query,
        IContactRepository contacts,
        CancellationToken ct
    )
    {
        var page = await contacts.ListAsync(query.TenantId, query.Page, query.Size, ct);
        return new PagedResult<ContactResponse>(
            page.Items.Select(ContactResponse.From).ToList(),
            page.Page,
            page.Size,
            page.TotalCount
        );
    }
}

// ─────────────────────────── Contact lists ───────────────────────────

public sealed record ListContactListsQuery(Guid TenantId, int Page, int Size);

public static class ListContactListsHandler
{
    public static async Task<PagedResult<ContactListResponse>> Handle(
        ListContactListsQuery query,
        IContactListRepository lists,
        CancellationToken ct
    )
    {
        var page = await lists.ListAsync(query.TenantId, query.Page, query.Size, ct);
        return new PagedResult<ContactListResponse>(
            page.Items.Select(ContactListResponse.From).ToList(),
            page.Page,
            page.Size,
            page.TotalCount
        );
    }
}

/// <summary>Miembros (contactos) de una lista — para la edición de audiencia en el front.</summary>
public sealed record ListContactListMembersQuery(Guid TenantId, Guid ListId);

public static class ListContactListMembersHandler
{
    public static async Task<Result<IReadOnlyList<ContactResponse>>> Handle(
        ListContactListMembersQuery query,
        IContactListRepository lists,
        IContactRepository contacts,
        CancellationToken ct
    )
    {
        var list = await lists.GetByIdAsync(query.TenantId, query.ListId, ct);
        if (list is null)
            return Result.Failure<IReadOnlyList<ContactResponse>>(ContactListErrors.NotFound);

        var ids = list.Members.Select(m => m.ContactId).ToList();
        if (ids.Count == 0)
            return Result.Success<IReadOnlyList<ContactResponse>>([]);

        var members = await contacts.GetManyByIdsAsync(query.TenantId, ids, ct);
        return Result.Success<IReadOnlyList<ContactResponse>>(members.Select(ContactResponse.From).ToList());
    }
}

public sealed record GetContactListQuery(Guid TenantId, Guid Id);

public static class GetContactListHandler
{
    public static async Task<Result<ContactListResponse>> Handle(
        GetContactListQuery query,
        IContactListRepository lists,
        CancellationToken ct
    )
    {
        var list = await lists.GetByIdAsync(query.TenantId, query.Id, ct);
        return list is null
            ? Result.Failure<ContactListResponse>(ContactListErrors.NotFound)
            : Result.Success(ContactListResponse.From(list));
    }
}
