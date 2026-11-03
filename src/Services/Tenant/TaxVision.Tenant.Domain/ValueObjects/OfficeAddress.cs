using System.Text.RegularExpressions;
using BuildingBlocks.Results;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// Dirección de la oficina (física o postal). Cada parte es opcional y se conserva aunque falten otras;
/// una parte vacía queda en null. Si el país es EE. UU. valida el código de estado de USPS y el ZIP.
/// Sin calle, ciudad, estado ni ZIP no hay dirección (null).
/// </summary>
public sealed partial class OfficeAddress
{
    public const string UnitedStates = "US";
    public const int LineMaxLength = 200;
    public const int CityMaxLength = 100;
    public const int CountryMaxLength = 60;

    public static readonly Error InvalidState = new(
        "Tenant.Profile.State",
        "Use the 2-letter US state code, like TX or NY."
    );

    public static readonly Error InvalidPostalCode = new(
        "Tenant.Profile.PostalCode",
        "Enter a 5-digit ZIP code, or ZIP+4 like 78701-1234."
    );

    public static readonly Error TooLong = new(
        "Tenant.Profile.Address",
        $"Address lines can have up to {LineMaxLength} characters, the city {CityMaxLength} and the country {CountryMaxLength}."
    );

    // Formas habituales de escribir EE. UU. a mano; todas se guardan como "US".
    private static readonly HashSet<string> UnitedStatesAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "US",
        "USA",
        "U.S.",
        "U.S.A.",
        "UNITED STATES",
        "UNITED STATES OF AMERICA",
    };

    // EF Core materializa la dirección guardada sin volver a validarla.
    private OfficeAddress() { }

    public string? Line1 { get; private set; }
    public string? Line2 { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? PostalCode { get; private set; }
    public string Country { get; private set; } = UnitedStates;

    public static Result<OfficeAddress?> CreateOptional(
        string? line1,
        string? line2,
        string? city,
        string? state,
        string? postalCode,
        string? country
    )
    {
        var normalizedCountry = NormalizeCountry(country);
        var normalizedState = Clean(state)?.ToUpperInvariant();
        var normalizedPostalCode = Clean(postalCode);
        var address = new OfficeAddress
        {
            Line1 = Clean(line1),
            Line2 = Clean(line2),
            City = Clean(city),
            State = normalizedState,
            PostalCode = normalizedPostalCode,
            Country = normalizedCountry,
        };

        if (address.IsTooLong())
            return Result.Failure<OfficeAddress?>(TooLong);

        if (normalizedCountry == UnitedStates)
        {
            if (normalizedState is not null && !UsStateCodes.IsValid(normalizedState))
                return Result.Failure<OfficeAddress?>(InvalidState);

            if (normalizedPostalCode is not null)
            {
                if (!UsZipPattern().IsMatch(normalizedPostalCode))
                    return Result.Failure<OfficeAddress?>(InvalidPostalCode);
                // ZIP+4 escrito sin guion → con guion.
                if (normalizedPostalCode.Length == 9)
                    address.PostalCode = $"{normalizedPostalCode[..5]}-{normalizedPostalCode[5..]}";
            }
        }

        return Result.Success<OfficeAddress?>(address.IsBlank() ? null : address);
    }

    /// <summary>Igualdad por contenido (EF rastrea esta clase por referencia, así que no se pisa Equals).</summary>
    public bool Matches(OfficeAddress? other) =>
        other is not null
        && Line1 == other.Line1
        && Line2 == other.Line2
        && City == other.City
        && State == other.State
        && PostalCode == other.PostalCode
        && Country == other.Country;

    private bool IsBlank() => Line1 is null && Line2 is null && City is null && State is null && PostalCode is null;

    private bool IsTooLong() =>
        Line1?.Length > LineMaxLength
        || Line2?.Length > LineMaxLength
        || City?.Length > CityMaxLength
        || Country.Length > CountryMaxLength;

    private static string NormalizeCountry(string? country)
    {
        var trimmed = Clean(country);
        if (trimmed is null || UnitedStatesAliases.Contains(trimmed))
            return UnitedStates;
        return trimmed.ToUpperInvariant();
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    [GeneratedRegex(@"^\d{5}(-?\d{4})?$")]
    private static partial Regex UsZipPattern();
}
