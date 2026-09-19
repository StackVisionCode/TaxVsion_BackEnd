using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Application.Contacts.Commands;

// ─────────────────────────── Create ───────────────────────────

/// <summary>Alta manual de un contacto (source Manual). TenantId viene del JWT en el controller.</summary>
public sealed record CreateContactCommand(Guid TenantId, string? Name, string? Email, string? PhoneE164);

public static class CreateContactHandler
{
    public static async Task<Result<ContactResponse>> Handle(
        CreateContactCommand command,
        IContactRepository contacts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var result = Contact.Create(command.TenantId, command.Name, command.Email, command.PhoneE164, ContactSource.Manual);
        if (result.IsFailure)
            return Result.Failure<ContactResponse>(result.Error);

        await contacts.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct); // dupe de destino → ConflictException (índice único filtrado)
        return Result.Success(ContactResponse.From(result.Value));
    }
}

// ─────────────────────────── Update ───────────────────────────

public sealed record UpdateContactCommand(Guid TenantId, Guid ContactId, string? Name, string? Email, string? PhoneE164);

public static class UpdateContactHandler
{
    public static async Task<Result<ContactResponse>> Handle(
        UpdateContactCommand command,
        IContactRepository contacts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var contact = await contacts.GetByIdAsync(command.TenantId, command.ContactId, ct);
        if (contact is null)
            return Result.Failure<ContactResponse>(ContactErrors.NotFound);

        var updated = contact.Update(command.Name, command.Email, command.PhoneE164);
        if (updated.IsFailure)
            return Result.Failure<ContactResponse>(updated.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ContactResponse.From(contact));
    }
}

// ─────────────────────────── Opt-out / Opt-in ───────────────────────────

/// <summary>Cambia el consentimiento por canal. <paramref name="OptedOut"/> true = baja; false = re-suscribe.</summary>
public sealed record SetContactOptOutCommand(Guid TenantId, Guid ContactId, CampaignChannel Channels, bool OptedOut);

public static class SetContactOptOutHandler
{
    public static async Task<Result<ContactResponse>> Handle(
        SetContactOptOutCommand command,
        IContactRepository contacts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var contact = await contacts.GetByIdAsync(command.TenantId, command.ContactId, ct);
        if (contact is null)
            return Result.Failure<ContactResponse>(ContactErrors.NotFound);

        if (command.OptedOut)
            contact.OptOut(command.Channels);
        else
            contact.OptIn(command.Channels);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ContactResponse.From(contact));
    }
}
