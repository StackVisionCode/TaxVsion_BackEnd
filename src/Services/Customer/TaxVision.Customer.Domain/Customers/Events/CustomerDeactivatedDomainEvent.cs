using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.Customers.Events;

public sealed record CustomerDeactivatedDomainEvent(
    Guid CustomerId,
    Guid TenantId,
    Guid DeactivatedByUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;
