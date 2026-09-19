using BuildingBlocks.Common;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Application.Contacts.Abstractions;

/// <summary>Repositorio del aggregate <see cref="ContactList"/> (incluye sus membresías).</summary>
public interface IContactListRepository
{
    /// <summary>Carga una lista con sus membresías por <c>(TenantId, Id)</c>.</summary>
    Task<ContactList?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Ids de contactos miembros de un conjunto de listas del tenant (para resolver audiencia).</summary>
    Task<IReadOnlyList<Guid>> GetMemberContactIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> listIds,
        CancellationToken ct = default
    );

    Task<PagedResult<ContactList>> ListAsync(Guid tenantId, int page, int size, CancellationToken ct = default);

    Task AddAsync(ContactList list, CancellationToken ct = default);

    /// <summary>Elimina la lista (sus membresías caen por cascade).</summary>
    void Remove(ContactList list);

    /// <summary>Elimina cualquier membresía que apunte a un contacto (al borrar el contacto).</summary>
    Task RemoveMembershipsForContactAsync(Guid tenantId, Guid contactId, CancellationToken ct = default);
}
