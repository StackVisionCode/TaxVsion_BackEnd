using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.CustomerVisibility;

/// <summary>Lee/escribe la proyección compartida de asignaciones sobre CUALQUIER DbContext que la mapee.</summary>
public interface ICustomerAssignmentProjectionStore
{
    Task<DateTime?> GetVersionAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// ¿Alguno de estos clientes está asignado a este usuario? Gate de detalle por-recurso (ABAC per-cliente):
    /// el read path de cada servicio reúne los clientes que toca el recurso y pregunta aquí. Conjunto vacío ⇒
    /// false (el recurso sin cliente mapeado no es visible para quien no ve todo, igual que el filtro de lista).
    /// </summary>
    Task<bool> IsAnyAssignedToUserAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken ct = default
    );

    /// <summary>
    /// El SET de clientes asignados a este usuario en el tenant. Para filtrar una lista materializada en
    /// memoria (p.ej. la audiencia de una campaña) por asignación, cuando se necesita el conjunto y no una
    /// simple comprobación de pertenencia.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );

    /// <summary>Reemplaza el set de asignados del cliente (borra + inserta) con la versión dada. NO guarda.</summary>
    Task ReplaceAsync(
        Guid tenantId,
        Guid customerId,
        IReadOnlyCollection<Guid> userIds,
        DateTime version,
        CancellationToken ct = default
    );
}

/// <summary>
/// IgnoreQueryFilters() + tenantId explícito: corre en scopes de Wolverine (consumer) y BackgroundService
/// (reconciliación) sin ITenantContext ambiental — el filtro global fail-closed daría 0 filas. El tenantId
/// viene explícito del evento/fuente. Toma el DbContext base y usa Set&lt;&gt; (no depende del DbContext concreto).
/// </summary>
public sealed class CustomerAssignmentProjectionStore(DbContext db) : ICustomerAssignmentProjectionStore
{
    public async Task<DateTime?> GetVersionAsync(Guid tenantId, Guid customerId, CancellationToken ct = default)
    {
        var versions = await db.Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.CustomerId == customerId)
            .Select(a => (DateTime?)a.Version)
            .ToListAsync(ct);
        return versions.Count == 0 ? null : versions.Max();
    }

    public async Task<bool> IsAnyAssignedToUserAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken ct = default
    )
    {
        if (customerIds.Count == 0)
            return false;
        return await db.Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .AnyAsync(a => a.TenantId == tenantId && a.UserId == userId && customerIds.Contains(a.CustomerId), ct);
    }

    public async Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db.Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.UserId == userId)
            .Select(a => a.CustomerId)
            .Distinct()
            .ToListAsync(ct);

    public async Task ReplaceAsync(
        Guid tenantId,
        Guid customerId,
        IReadOnlyCollection<Guid> userIds,
        DateTime version,
        CancellationToken ct = default
    )
    {
        var set = db.Set<CustomerAssignmentProjection>();
        var existing = await set.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.CustomerId == customerId)
            .ToListAsync(ct);
        set.RemoveRange(existing);

        foreach (var userId in userIds.Distinct())
            await set.AddAsync(CustomerAssignmentProjection.Create(tenantId, customerId, userId, version), ct);
    }
}
