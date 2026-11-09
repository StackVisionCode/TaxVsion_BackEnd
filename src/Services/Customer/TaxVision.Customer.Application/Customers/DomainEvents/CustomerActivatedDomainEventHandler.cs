using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Customer.Domain.Customers.Events;

namespace TaxVision.Customer.Application.Customers.DomainEvents;

public static class CustomerActivatedDomainEventHandler
{
    public static CustomerActivatedIntegrationEvent Handle(
        CustomerActivatedDomainEvent activated,
        ICorrelationContext correlation
    ) =>
        new()
        {
            TenantId = activated.TenantId,
            CorrelationId = correlation.CorrelationId,
            OccurredOn = activated.OccurredAtUtc,
            CustomerId = activated.CustomerId,
            ActivatedByUserId = activated.ActivatedByUserId,
            ActivatedAtUtc = activated.OccurredAtUtc,
        };
}
