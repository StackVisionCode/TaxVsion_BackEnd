using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.Customers.Events;

/// <summary>Nació el cliente. Los datos del aviso se leen del cliente guardado.</summary>
public sealed record CustomerCreatedDomainEvent(
    Guid CustomerId,
    Guid TenantId,
    Guid CreatedByUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
