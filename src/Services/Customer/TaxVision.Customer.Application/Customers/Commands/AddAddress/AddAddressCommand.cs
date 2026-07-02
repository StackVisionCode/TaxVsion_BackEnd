using TaxVision.Customer.Domain.Addresses;

namespace TaxVision.Customer.Application.Customers.Commands.AddAddress;

public sealed record AddAddressCommand(
    Guid TenantId,
    Guid CustomerId,
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
