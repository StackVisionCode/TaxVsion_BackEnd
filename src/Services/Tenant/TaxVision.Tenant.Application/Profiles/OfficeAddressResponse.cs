namespace TaxVision.Tenant.Application.Profiles;

public sealed record OfficeAddressResponse(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string Country
);
