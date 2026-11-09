using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Customer.Domain.Customers.Events;

namespace TaxVision.Customer.Application.Customers.DomainEvents;

public static class CustomerArchivedDomainEventHandler
{
    public static CustomerArchivedIntegrationEvent Handle(
        CustomerArchivedDomainEvent archived,
        ICorrelationContext correlation
    ) =>
        new()
        {
            TenantId = archived.TenantId,
            CorrelationId = correlation.CorrelationId,
            OccurredOn = archived.OccurredAtUtc,
            CustomerId = archived.CustomerId,
            ArchivedByUserId = archived.ArchivedByUserId,
        };
}
