using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers;

/// <summary>
/// Un solo lugar que arma <see cref="CustomerUpdatedIntegrationEvent"/> desde el agregado: así ningún
/// publicador se olvida de un campo (la foto, la política de contacto…).
/// </summary>
public static class CustomerUpdatedEvents
{
    public static CustomerUpdatedIntegrationEvent From(
        CustomerEntity customer,
        Guid modifiedByUserId,
        string correlationId
    ) =>
        new()
        {
            TenantId = customer.TenantId,
            CorrelationId = correlationId,
            CustomerId = customer.Id,
            DisplayName = customer.DisplayName,
            PrimaryEmail = customer.PrimaryEmail.Value,
            PrimaryPhone = customer.PrimaryPhone?.E164Value,
            Language = customer.Language.ToString(),
            EmailEnabled = customer.ContactPolicy.EmailEnabled,
            SmsEnabled = customer.ContactPolicy.SmsEnabled,
            OccupationId = customer.OccupationId,
            AvatarFileId = customer.ProfilePictureFileId,
            ModifiedByUserId = modifiedByUserId,
        };
}
