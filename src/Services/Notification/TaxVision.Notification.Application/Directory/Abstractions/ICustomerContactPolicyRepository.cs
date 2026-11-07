using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Application.Directory.Abstractions;

/// <summary>
/// Lee/mantiene la proyección de <c>Customer.ContactPolicy</c> (T7.3). Un cliente, una fila.
/// Si no existe, la política efectiva es "ambos ON" (opt-out).
/// </summary>
public interface ICustomerContactPolicyRepository
{
    Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    );

    /// <summary>De los clientes dados, los que ya tienen fila. Una sola consulta por lote.</summary>
    Task<IReadOnlyCollection<Guid>> GetExistingCustomerIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken ct = default
    );

    Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default);
}
