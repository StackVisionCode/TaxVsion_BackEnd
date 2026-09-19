using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Application.Contacts.Commands;

// ─────────────────────────── Create list ───────────────────────────

public sealed record CreateContactListCommand(Guid TenantId, string Name, string? Description);

public static class CreateContactListHandler
{
    public static async Task<Result<ContactListResponse>> Handle(
        CreateContactListCommand command,
        IContactListRepository lists,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var result = ContactList.Create(command.TenantId, command.Name, command.Description);
        if (result.IsFailure)
            return Result.Failure<ContactListResponse>(result.Error);

        await lists.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ContactListResponse.From(result.Value));
    }
}

// ─────────────────────────── Add member ───────────────────────────

public sealed record AddContactToListCommand(Guid TenantId, Guid ContactListId, Guid ContactId);

public static class AddContactToListHandler
{
    public static async Task<Result<ContactListResponse>> Handle(
        AddContactToListCommand command,
        IContactListRepository lists,
        IContactRepository contacts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var list = await lists.GetByIdAsync(command.TenantId, command.ContactListId, ct);
        if (list is null)
            return Result.Failure<ContactListResponse>(ContactListErrors.NotFound);

        // El contacto debe existir y pertenecer al tenant (evita membresías colgantes cross-tenant).
        var contact = await contacts.GetByIdAsync(command.TenantId, command.ContactId, ct);
        if (contact is null)
            return Result.Failure<ContactListResponse>(ContactErrors.NotFound);

        var added = list.AddMember(command.ContactId);
        if (added.IsFailure)
            return Result.Failure<ContactListResponse>(added.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ContactListResponse.From(list));
    }
}

// ─────────────────────────── Remove member ───────────────────────────

public sealed record RemoveContactFromListCommand(Guid TenantId, Guid ContactListId, Guid ContactId);

public static class RemoveContactFromListHandler
{
    public static async Task<Result<ContactListResponse>> Handle(
        RemoveContactFromListCommand command,
        IContactListRepository lists,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var list = await lists.GetByIdAsync(command.TenantId, command.ContactListId, ct);
        if (list is null)
            return Result.Failure<ContactListResponse>(ContactListErrors.NotFound);

        var removed = list.RemoveMember(command.ContactId);
        if (removed.IsFailure)
            return Result.Failure<ContactListResponse>(removed.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ContactListResponse.From(list));
    }
}
