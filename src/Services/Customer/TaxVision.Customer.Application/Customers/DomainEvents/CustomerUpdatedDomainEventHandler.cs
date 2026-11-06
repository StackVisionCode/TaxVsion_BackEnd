using BuildingBlocks.Common;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers.Events;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.DomainEvents;

/// <summary>Se editó el cliente: <c>CustomerUpdated</c> con el cliente guardado (estado completo).</summary>
public static class CustomerUpdatedDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        CustomerUpdatedDomainEvent updated,
        ICustomerRepository customers,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        var customer = await customers.GetSnapshotAsync(updated.TenantId, updated.CustomerId, ct);
        if (customer is not null)
            messages.Add(
                CustomerUpdatedEvents.From(customer, updated.ModifiedByUserId, correlation.CorrelationId) with
                {
                    OccurredOn = updated.OccurredAtUtc,
                }
            );
        return messages;
    }
}
