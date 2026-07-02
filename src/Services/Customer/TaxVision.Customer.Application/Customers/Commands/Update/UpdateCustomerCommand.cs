using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers.Commands.Update;

public sealed record UpdateCustomerCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ModifiedByUserId,
    Language Language,
    PreferredChannel PreferredChannel,
    Guid? OccupationId,
    Guid? ProfilePictureFileId,
    string PrimaryEmail,
    string? PrimaryPhone
);
