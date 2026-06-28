using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Api.Requests;

public sealed record UpdateCustomerRequest(
    Language Language,
    PreferredChannel PreferredChannel,
    Guid? OccupationId,
    Guid? ProfilePictureFileId,
    string PrimaryEmail,
    string? PrimaryPhone
);
