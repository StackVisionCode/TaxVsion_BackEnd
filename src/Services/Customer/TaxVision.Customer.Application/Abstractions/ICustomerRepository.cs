using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerRepository
{
    Task<CustomerEntity?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Carga en una sola query los customers cuyos ids esten en la coleccion, filtrando por tenant.
    /// Los ids no encontrados no aparecen en el resultado. Usado por operaciones bulk para evitar N+1.
    /// </summary>
    Task<IReadOnlyList<CustomerEntity>> GetByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct
    );

    /// <summary>
    /// Busca en el tenant si otro customer (distinto de excludeCustomerId) tiene un fiscal profile
    /// con el mismo blind index. Devuelve el CustomerId conflictivo o null si no hay conflicto.
    /// Sirve como pre-check en aplicacion antes que dispare el UNIQUE INDEX de BD.
    /// </summary>
    Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
        Guid tenantId,
        string blindIndex,
        Guid? excludeCustomerId,
        CancellationToken ct
    );

    /// <summary>
    /// Similar al metodo anterior pero para relaciones (spouses/dependientes). Devuelve el
    /// RelationId conflictivo o null si no hay conflicto en el tenant.
    /// </summary>
    Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(
        Guid tenantId,
        string blindIndex,
        Guid? excludeRelationId,
        CancellationToken ct
    );

    Task AddAsync(CustomerEntity customer, CancellationToken ct);
}
