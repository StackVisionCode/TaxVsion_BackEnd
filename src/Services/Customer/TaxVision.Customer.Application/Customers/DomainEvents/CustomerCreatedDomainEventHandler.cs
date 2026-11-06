using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers.Events;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.DomainEvents;

/// <summary>
/// Nació un cliente (alta o formulario): <c>CustomerCreated</c> con el cliente guardado. Corre después del
/// commit, desde la cola domain-events.
/// </summary>
public static class CustomerCreatedDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        CustomerCreatedDomainEvent created,
        ICustomerRepository customers,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        var customer = await customers.GetSnapshotAsync(created.TenantId, created.CustomerId, ct);
        if (customer is null)
            return messages;

        messages.Add(
            new CustomerCreatedIntegrationEvent
            {
                TenantId = customer.TenantId,
                CorrelationId = correlation.CorrelationId,
                OccurredOn = created.OccurredAtUtc,
                CustomerId = customer.Id,
                Kind = customer.Kind.ToString(),
                DisplayName = customer.DisplayName,
                PrimaryEmail = customer.PrimaryEmail.Value,
                PrimaryPhone = customer.PrimaryPhone?.E164Value,
                Language = customer.Language.ToString(),
                EmailEnabled = customer.ContactPolicy.EmailEnabled,
                SmsEnabled = customer.ContactPolicy.SmsEnabled,
                OccupationId = customer.OccupationId,
                AvatarFileId = customer.ProfilePictureFileId,
                CreatedByUserId = customer.CreatedByUserId,
            }
        );
        return messages;
    }
}
