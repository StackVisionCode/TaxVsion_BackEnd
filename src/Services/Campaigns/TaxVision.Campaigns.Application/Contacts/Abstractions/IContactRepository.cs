using BuildingBlocks.Common;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Application.Contacts.Abstractions;

/// <summary>
/// Repositorio del aggregate <see cref="Contact"/>. Toda lectura filtra por <c>TenantId</c> explícito
/// (aislamiento multitenant a nivel de repo).
/// </summary>
public interface IContactRepository
{
    Task<Contact?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Busca un contacto existente por email o teléfono normalizados (dedupe de import/alta). Cualquiera puede ser null.</summary>
    Task<Contact?> FindByDestinationAsync(
        Guid tenantId,
        string? email,
        string? phoneE164,
        CancellationToken ct = default
    );

    /// <summary>Carga varios contactos por id (para resolver audiencia de un run). Solo los del tenant.</summary>
    Task<IReadOnlyList<Contact>> GetManyByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default
    );

    Task<PagedResult<Contact>> ListAsync(Guid tenantId, int page, int size, CancellationToken ct = default);

    Task AddAsync(Contact contact, CancellationToken ct = default);

    void Remove(Contact contact);
}
