using TaxVision.Customer.Domain.Addresses;

namespace TaxVision.Customer.Application.Customers;

public sealed record AddressResponse(
    Guid Id,
    AddressKind Kind,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    bool IsPrimary
);
