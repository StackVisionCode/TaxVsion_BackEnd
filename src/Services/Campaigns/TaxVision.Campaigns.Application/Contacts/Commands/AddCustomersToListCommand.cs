using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Domain;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Application.Contacts.Commands;

/// <summary>
/// Agrega clientes SELECCIONADOS (del directorio de Customer) como miembros de una lista de contactos.
/// Para cada cliente se materializa un <see cref="Contact"/> con source <see cref="ContactSource.FromCustomer"/>
/// enlazado por <c>CustomerId</c> (dedupe por destino: si ya existe un contacto con ese email/teléfono se
/// reusa) y se agrega a la lista (idempotente). No crea clientes — ya lo son.
/// </summary>
public sealed record AddCustomersToListCommand(Guid TenantId, Guid ContactListId, IReadOnlyList<Guid> CustomerIds);

/// <summary>Resumen del alta de clientes a la lista.</summary>
public sealed record AddCustomersToListResponse(
    int ContactsCreated,
    int ContactsReused,
    int MembersAdded,
    int NotFound,
    int Invalid
);

public static class AddCustomersToListHandler
{
    public static async Task<Result<AddCustomersToListResponse>> Handle(
        AddCustomersToListCommand command,
        IContactListRepository lists,
        IContactRepository contacts,
        ICustomerAudienceClient customerClient,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var list = await lists.GetByIdAsync(command.TenantId, command.ContactListId, ct);
        if (list is null)
            return Result.Failure<AddCustomersToListResponse>(ContactListErrors.NotFound);

        var selected = (command.CustomerIds ?? []).Where(id => id != Guid.Empty).ToHashSet();
        if (selected.Count == 0)
            return Result.Success(new AddCustomersToListResponse(0, 0, 0, 0, 0));

        // Resolución de los clientes elegidos desde el directorio de Customer (M2M). Se filtra a la selección.
        var active = await customerClient.GetActiveCustomersAsync(command.TenantId, ct);
        var chosen = active.Where(c => selected.Contains(c.CustomerId)).ToList();

        int created = 0,
            reused = 0,
            membersAdded = 0,
            invalid = 0;

        foreach (var member in chosen)
        {
            // Create normaliza email/teléfono y valida que haya al menos un destino.
            var draft = Contact.Create(
                command.TenantId,
                member.DisplayName,
                member.Email,
                member.PhoneE164,
                ContactSource.FromCustomer,
                customerRef: member.CustomerId
            );
            if (draft.IsFailure)
            {
                invalid++;
                continue;
            }
            var normalized = draft.Value;

            // Dedupe por EMAIL (un cliente siempre tiene email único). NO por teléfono: dos personas
            // distintas pueden compartir/placeholderear el número y reusaríamos el contacto equivocado.
            Contact? contact =
                normalized.Email is not null
                    ? await contacts.FindByDestinationAsync(command.TenantId, normalized.Email, null, ct)
                    : await contacts.FindByDestinationAsync(command.TenantId, null, normalized.PhoneE164, ct);

            if (contact is null)
            {
                // El teléfono tiene unique por tenant: si ya pertenece a OTRO contacto, se crea sin teléfono
                // (se conserva el email) en vez de romper con un 409. El email sí identifica al cliente.
                var phone = normalized.PhoneE164;
                if (phone is not null && await contacts.FindByDestinationAsync(command.TenantId, null, phone, ct) is not null)
                    phone = null;

                var toCreate =
                    phone == normalized.PhoneE164
                        ? normalized
                        : Contact.Create(
                            command.TenantId,
                            member.DisplayName,
                            normalized.Email,
                            phone,
                            ContactSource.FromCustomer,
                            customerRef: member.CustomerId
                        ) is { IsSuccess: true } rebuilt
                            ? rebuilt.Value
                            : null;

                if (toCreate is null)
                {
                    invalid++;
                    continue;
                }

                contact = toCreate;
                await contacts.AddAsync(contact, ct);
                created++;
            }
            else
            {
                reused++;
            }

            var before = list.Members.Count;
            list.AddMember(contact.Id);
            if (list.Members.Count > before)
                membersAdded++;
        }

        await unitOfWork.SaveChangesAsync(ct);

        // notFound = seleccionados que el directorio no devolvió como activos (archivados/ajenos/inexistentes).
        var notFound = selected.Count - chosen.Select(c => c.CustomerId).Distinct().Count();
        return Result.Success(new AddCustomersToListResponse(created, reused, membersAdded, notFound, invalid));
    }
}
