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

    /// <summary>Clientes ACTIVOS donde <paramref name="preparerUserId"/> es el preparador PRIMARY, por lotes
    /// (keyset por Id) — para el handover al retirarlo. Tracked + Include(Assignments): las mutaciones de
    /// preparador tocan la colección de asignaciones, si no viene cargada operan sobre una vacía.</summary>
    Task<IReadOnlyList<CustomerEntity>> ListByAssignedPreparerAsync(
        Guid tenantId,
        Guid preparerUserId,
        int batchSize,
        Guid afterId,
        CancellationToken ct
    );

    /// <summary>Clientes ACTIVOS donde el usuario tiene un acceso ADICIONAL (fila no-primary), por lotes
    /// (keyset por Id) — para revocárselos al retirarlo (el primary va por ListByAssignedPreparerAsync).
    /// Tracked + Include(Assignments). Default vacío para no romper los fakes.</summary>
    Task<IReadOnlyList<CustomerEntity>> ListNonPrimaryAssignedAsync(
        Guid tenantId,
        Guid userId,
        int batchSize,
        Guid afterId,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<CustomerEntity>>([]);

    /// <summary>Cuántos clientes ACTIVOS tiene asignados este usuario (primary O acceso adicional) — para el
    /// pre-flight de impacto al retirarlo. Default 0 para no romper los fakes; el repo real lo implementa
    /// con COUNT.</summary>
    Task<int> CountActiveByAssignedPreparerAsync(Guid tenantId, Guid preparerUserId, CancellationToken ct) =>
        Task.FromResult(0);

    Task AddAsync(CustomerEntity customer, CancellationToken ct);

    /// <summary>Carga en 1 query los clientes NO archivados del tenant cuyos ids estén en la colección, con
    /// sus asignaciones (Include) — para el reparto masivo: se mutan (AssignPreparer/GrantAccess) y se guardan.
    /// Tracked. Default vacío para no romper los fakes.</summary>
    Task<IReadOnlyList<CustomerEntity>> ListForAssignmentAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<CustomerEntity>>([]);
}
