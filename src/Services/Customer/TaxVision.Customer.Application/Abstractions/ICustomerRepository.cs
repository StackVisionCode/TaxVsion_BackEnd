using TaxVision.Customer.Application.Customers.Queries.OffboardingImpact;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerRepository
{
    /// <summary>El agregado completo, solo si es de ese tenant.</summary>
    Task<CustomerEntity?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct);

    /// <summary>
    /// Cliente con sus asignaciones, sin seguimiento y con el tenant explícito: lo usan los handlers de eventos
    /// de dominio para armar los avisos, que corren sin TenantContext.
    /// </summary>
    Task<CustomerEntity?> GetSnapshotAsync(Guid tenantId, Guid customerId, CancellationToken ct);

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

    /// <summary>
    /// Lookup dual (B-9/X-9): busca por cualquiera de los blind indexes candidatos (clave actual ∪ anterior)
    /// para no dar falso negativo mientras el sweep de rotación aún no re-cifró la fila. Sin rotación el
    /// conjunto trae un solo candidato y equivale al metodo de un solo blind index.
    /// </summary>
    Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
        Guid tenantId,
        IReadOnlyCollection<string> blindIndexes,
        Guid? excludeCustomerId,
        CancellationToken ct
    );

    /// <summary>Lookup dual para relaciones (ver el metodo de customer).</summary>
    Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(
        Guid tenantId,
        IReadOnlyCollection<string> blindIndexes,
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
    /// Tracked + Include(Assignments).</summary>
    Task<IReadOnlyList<CustomerEntity>> ListNonPrimaryAssignedAsync(
        Guid tenantId,
        Guid userId,
        int batchSize,
        Guid afterId,
        CancellationToken ct
    );

    /// <summary>Cuántos clientes ACTIVOS tiene asignados este usuario (primary O acceso adicional) — para el
    /// pre-flight de impacto al retirarlo.</summary>
    Task<int> CountActiveByAssignedPreparerAsync(Guid tenantId, Guid preparerUserId, CancellationToken ct);

    /// <summary>Clientes ARCHIVADOS donde el usuario es primary o tiene cualquier fila de asignación, por lotes
    /// (keyset por Id) — para soltarlo al retirarlo y que no vuelvan con él al reactivarlos. Tracked +
    /// Include(Assignments).</summary>
    Task<IReadOnlyList<CustomerEntity>> ListArchivedAssignedAsync(
        Guid tenantId,
        Guid userId,
        int batchSize,
        Guid afterId,
        CancellationToken ct
    );

    /// <summary>Desglose del pre-flight de impacto al retirar: primary vs accesos adicionales (activos) y
    /// archivados que se sueltan.</summary>
    Task<OffboardingClientCounts> CountOffboardingImpactAsync(Guid tenantId, Guid userId, CancellationToken ct);

    Task AddAsync(CustomerEntity customer, CancellationToken ct);

    /// <summary>Cliente creado por esa operación de origen, sin filtrar por estado: archivado o con otro
    /// email se sigue encontrando.</summary>
    Task<Guid?> FindIdByOriginAsync(Guid tenantId, string sourceService, Guid sourceReferenceId, CancellationToken ct);

    /// <summary>Carga en 1 query los clientes NO archivados del tenant cuyos ids estén en la colección, con
    /// sus asignaciones (Include) — para el reparto masivo: se mutan (AssignPreparer/GrantAccess) y se guardan.
    /// Tracked.</summary>
    Task<IReadOnlyList<CustomerEntity>> ListForAssignmentAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct
    );
}
