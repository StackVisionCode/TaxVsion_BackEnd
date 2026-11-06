using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.Customers.Events;

public sealed record CustomerReactivatedDomainEvent(
    Guid CustomerId,
    Guid TenantId,
    Guid ReactivatedByUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
