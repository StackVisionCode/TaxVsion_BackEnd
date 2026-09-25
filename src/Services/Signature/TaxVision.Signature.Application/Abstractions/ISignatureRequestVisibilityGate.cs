namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Gate de visibilidad por asignación (ABAC per-cliente) para el detalle de una solicitud. Dado el conjunto de
/// clientes que la solicitud toca (los <c>MappedCustomerId</c> de sus firmantes), decide si el actor puede verla.
/// La impl consulta la proyección local de asignaciones + el flag de visibilidad; quien ve todo (customers.view_all
/// / admin) o el flag apagado ⇒ siempre visible. La lista se filtra en el read service; esto es el mismo criterio
/// para el detalle, evitando filtrar por ID un recurso de un cliente no asignado.
/// </summary>
public interface ISignatureRequestVisibilityGate
{
    Task<bool> CanActorSeeAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        IReadOnlyCollection<Guid> mappedCustomerIds,
        CancellationToken ct = default
    );
}
