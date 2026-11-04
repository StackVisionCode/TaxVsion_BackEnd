using TaxVision.Tenant.Domain.Profiles;

namespace TaxVision.Tenant.Api.Contracts;

public sealed record OfficeAddressRequest(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country
)
{
    public OfficeAddressDraft ToDraft() => new(Line1, Line2, City, State, PostalCode, Country);
}
