using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.Customers.Events;

/// <summary>
/// Cambió el staff asignado. Uno por transacción: el snapshot (asignados, primary y versión) se arma con el
/// cliente guardado, así la versión siempre corresponde al conjunto que viaja.
/// </summary>
public sealed record CustomerAssignmentsChangedDomainEvent(Guid CustomerId, Guid TenantId, DateTime OccurredAtUtc)
    : IDomainEvent;
