using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Customers.ValueObjects;

public sealed record AddressValue
{
    public string Line1 { get; }
    public string? Line2 { get; }
    public string City { get; }
    public string? Region { get; }
    public string PostalCode { get; }
    public string CountryCode { get; }

    private AddressValue(
        string line1,
        string? line2,
        string city,
        string? region,
        string postalCode,
        string countryCode
    )
    {
        Line1 = line1;
        Line2 = string.IsNullOrWhiteSpace(line2) ? null : line2.Trim();
        City = city;
        Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
        PostalCode = postalCode;
        CountryCode = countryCode;
    }

    public static Result<AddressValue> Create(
        string line1,
        string city,
        string postalCode,
        string countryCode,
        string? line2 = null,
        string? region = null
    )
    {
        if (string.IsNullOrWhiteSpace(line1))
            return Result.Failure<AddressValue>(new Error("Address.Line1", "Address line 1 is required."));
        if (string.IsNullOrWhiteSpace(city))
            return Result.Failure<AddressValue>(new Error("Address.City", "City is required."));
        if (string.IsNullOrWhiteSpace(postalCode))
            return Result.Failure<AddressValue>(new Error("Address.PostalCode", "Postal code is required."));
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
            return Result.Failure<AddressValue>(new Error("Address.CountryCode", "Country must be ISO-3166 alpha-2."));

        return Result.Success(
            new AddressValue(
                line1.Trim(),
                line2,
                city.Trim(),
                region,
                postalCode.Trim(),
                countryCode.Trim().ToUpperInvariant()
            )
        );
    }
}
