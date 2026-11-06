using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Customer.Domain.Customers.Events;

namespace TaxVision.Customer.Application.Customers.DomainEvents;

public static class CustomerDeactivatedDomainEventHandler
{
    public static CustomerDeactivatedIntegrationEvent Handle(
        CustomerDeactivatedDomainEvent deactivated,
        ICorrelationContext correlation
    ) =>
        new()
        {
            TenantId = deactivated.TenantId,
            CorrelationId = correlation.CorrelationId,
            OccurredOn = deactivated.OccurredAtUtc,
            CustomerId = deactivated.CustomerId,
            DeactivatedByUserId = deactivated.DeactivatedByUserId,
            DeactivatedAtUtc = deactivated.OccurredAtUtc,
        };
}
