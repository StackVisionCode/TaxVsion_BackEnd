using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.Assignments;

/// <summary>
/// Asignación de un miembro del staff a un cliente (punto: acceso a clientes por asignación). Muchos-a-muchos:
/// un cliente puede estar asignado a N usuarios. Uno es <see cref="IsPrimary"/> = el preparador responsable
/// (se denormaliza en <c>Customer.AssignedPreparerUserId</c> para 8879/offboard/chat). Hija del agregado
/// <c>Customer</c>; se muta vía sus métodos. La visibilidad la usa: staff ve un cliente ⇔ es admin O tiene una fila acá.
/// </summary>
public sealed class CustomerAssignment : TenantEntity
{
    private CustomerAssignment() { }

    public Guid CustomerId { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsPrimary { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }

    internal static CustomerAssignment Create(
        Guid tenantId,
        Guid customerId,
        Guid userId,
        bool isPrimary,
        Guid assignedByUserId
    )
    {
        var entity = new CustomerAssignment
        {
            // Id sin asignar (igual que el resto de las hijas del agregado): la clave está mapeada
            // ValueGenerated.OnAdd, así que con un Guid ya puesto EF toma la fila por existente y
            // emite UPDATE en vez de INSERT — 0 filas afectadas y DbUpdateConcurrencyException.
            Id = Guid.Empty,
            CustomerId = customerId,
            UserId = userId,
            IsPrimary = isPrimary,
            AssignedByUserId = assignedByUserId,
            AssignedAtUtc = DateTime.UtcNow,
        };
        entity.SetTenant(tenantId);
        return entity;
    }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}
