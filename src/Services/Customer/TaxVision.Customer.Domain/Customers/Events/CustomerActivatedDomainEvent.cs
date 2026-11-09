using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.Customers.Events;

public sealed record CustomerActivatedDomainEvent(
    Guid CustomerId,
    Guid TenantId,
    Guid ActivatedByUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
