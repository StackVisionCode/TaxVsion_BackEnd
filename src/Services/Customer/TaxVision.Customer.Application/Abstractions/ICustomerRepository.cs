using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerRepository
{
    Task<CustomerEntity?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Carga en una sola query los customers cuyos ids esten en la coleccion, filtrando por tenant.
    /// Los ids no encontrados no aparecen en el resultado. Ordena por id para reproducibilidad.
    /// Usado por operaciones bulk para evitar N+1.
    /// </summary>
    Task<IReadOnlyList<CustomerEntity>> GetByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct
    );

    Task AddAsync(CustomerEntity customer, CancellationToken ct);
}
