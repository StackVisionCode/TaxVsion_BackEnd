using BuildingBlocks.Domain;

namespace BuildingBlocks.CustomerVisibility;

// Proyección local (P2) del set de staff asignado a un cliente, COMPARTIDA por los servicios downstream.
// La alimenta el snapshot CustomerAssignmentsChanged de Customer. Normalizada (fila por customer+user) para
// filtrar por JOIN. Version (UpdatedAtUtc del cliente) = idempotencia: solo se reemplaza con eventos más nuevos.
public sealed class CustomerAssignmentProjection : TenantEntity
{
    private CustomerAssignmentProjection() { }

    public Guid CustomerId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime Version { get; private set; }

    public static CustomerAssignmentProjection Create(Guid tenantId, Guid customerId, Guid userId, DateTime version)
    {
        var entity = new CustomerAssignmentProjection
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            UserId = userId,
            Version = version,
        };
        entity.SetTenant(tenantId);
        return entity;
    }
}
