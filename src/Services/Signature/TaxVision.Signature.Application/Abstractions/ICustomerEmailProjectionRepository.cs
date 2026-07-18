using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Proyección de clientes registrados del tenant. Alimentada por eventos de Customer;
/// consultada al agregar firmantes para vincular <c>MappedCustomerId</c> (regla P-14).
/// </summary>
public interface ICustomerEmailProjectionRepository
{
    Task<CustomerEmailProjection?> GetByCustomerIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// Busca el cliente registrado por email normalizado (trim + lowercase). Devuelve
    /// <c>null</c> si no existe o si está archivado.
    /// </summary>
    Task<CustomerEmailProjection?> FindActiveByEmailAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken ct = default
    );

    Task AddAsync(CustomerEmailProjection projection, CancellationToken ct = default);
}
