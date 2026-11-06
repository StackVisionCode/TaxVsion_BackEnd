using BuildingBlocks.Common;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers.Events;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.DomainEvents;

/// <summary>
/// Cambió el staff asignado: snapshot completo con la versión del mismo cliente leído (UpdatedAtUtc), así el
/// conjunto y su versión siempre viajan juntos.
/// </summary>
public static class CustomerAssignmentsChangedDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        CustomerAssignmentsChangedDomainEvent changed,
        ICustomerRepository customers,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        var customer = await customers.GetSnapshotAsync(changed.TenantId, changed.CustomerId, ct);
        if (customer is not null)
            messages.Add(
                CustomerAssignmentSnapshot.From(customer, correlation.CorrelationId) with
                {
                    OccurredOn = changed.OccurredAtUtc,
                }
            );
        return messages;
    }
}
