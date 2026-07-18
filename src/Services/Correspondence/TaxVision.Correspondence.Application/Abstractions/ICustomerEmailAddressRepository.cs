using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Proyección local de emails de clientes del tenant. Alimentada por los eventos de
/// Customer; consultada por el consumer de <c>raw_message_received</c> (Fase 4) para
/// resolver qué customer envió un correo entrante.
/// </summary>
public interface ICustomerEmailAddressRepository
{
    Task<CustomerEmailAddress?> GetByCustomerIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// Busca la fila activa (no soft-deleted) por email normalizado (trim + lowercase).
    /// Devuelve <c>null</c> si no existe o si está soft-deleted.
    /// </summary>
    Task<CustomerEmailAddress?> FindActiveByAddressAsync(
        Guid tenantId,
        string normalizedAddress,
        CancellationToken ct = default
    );

    Task AddAsync(CustomerEmailAddress entity, CancellationToken ct = default);
}
