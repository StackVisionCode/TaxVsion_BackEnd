using TaxVision.Customer.Domain.Addresses;

namespace TaxVision.Customer.Application.Customers.Commands.UpdateAddress;

public sealed record UpdateAddressCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid AddressId,
    Guid ModifiedByUserId,
    AddressKind Kind,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    bool IsPrimary
);
