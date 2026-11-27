using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.Customers.Events;

/// <summary>
/// Cambió algo de lo que el aviso <c>CustomerUpdated</c> lleva (contacto, nombre, idioma, foto…). Uno por
/// transacción: el aviso se arma con el cliente guardado.
/// </summary>
public sealed record CustomerUpdatedDomainEvent(
    Guid CustomerId,
    Guid TenantId,
    Guid ModifiedByUserId,
    DateTime OccurredAtUtc
) : IDomainEvent
{
    /// <summary>Correo con el que empezó la transacción (null si no tenía o en eventos viejos).</summary>
    public string? PreviousPrimaryEmail { get; init; }
}
