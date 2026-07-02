using TaxVision.Customer.Domain.Addresses;

namespace TaxVision.Customer.Api.Requests;

public sealed record AddAddressRequest(
    AddressKind Kind,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    bool IsPrimary
);
