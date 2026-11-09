using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Customer.Domain.Customers.Events;

namespace TaxVision.Customer.Application.Customers.DomainEvents;

public static class CustomerReactivatedDomainEventHandler
{
    public static CustomerReactivatedIntegrationEvent Handle(
        CustomerReactivatedDomainEvent reactivated,
        ICorrelationContext correlation
    ) =>
        new()
        {
            TenantId = reactivated.TenantId,
            CorrelationId = correlation.CorrelationId,
            OccurredOn = reactivated.OccurredAtUtc,
            CustomerId = reactivated.CustomerId,
            ReactivatedByUserId = reactivated.ReactivatedByUserId,
            ReactivatedAtUtc = reactivated.OccurredAtUtc,
        };
}
