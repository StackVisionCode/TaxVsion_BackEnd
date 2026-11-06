using BuildingBlocks.Domain;

namespace TaxVision.Customer.Domain.Customers.Events;

public sealed record CustomerArchivedDomainEvent(Guid CustomerId, Guid TenantId, Guid ArchivedByUserId, DateTime OccurredAtUtc)
    : IDomainEvent;
