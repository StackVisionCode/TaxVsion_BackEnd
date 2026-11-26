using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Sessions;

/// <summary>
/// Ubicación aproximada de una sesión, resuelta por su IP al crearla. Sin país no hay ubicación; las
/// coordenadas solo se guardan si son creíbles (nunca 0,0) y redondeadas a 2 decimales.
/// </summary>
public sealed class SessionLocation
{
    public const string DbIpSource = "DB-IP";
    private const int MaxTextLength = 128;

    private SessionLocation() { }

    public string CountryCode { get; private set; } = default!;
    public string? Region { get; private set; }
    public string? City { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public string Source { get; private set; } = DbIpSource;
    public DateTime ResolvedAtUtc { get; private set; }

    public static Result<SessionLocation> Create(
        string? countryCode,
        string? region,
        string? city,
        double? latitude,
        double? longitude,
        DateTime resolvedAtUtc
    )
    {
        var country = countryCode?.Trim().ToUpperInvariant();
        if (country is not { Length: 2 } || !country.All(char.IsAsciiLetterUpper))
            return Result.Failure<SessionLocation>(
                new Error("SessionLocation.Country", "Country must be two letters.")
            );

        var credible =
            latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180 && !(latitude == 0 && longitude == 0);

        return Result.Success(
            new SessionLocation
            {
                CountryCode = country,
                Region = Text(region),
                City = Text(city),
                Latitude = credible ? Math.Round(latitude!.Value, 2) : null,
                Longitude = credible ? Math.Round(longitude!.Value, 2) : null,
                Source = DbIpSource,
                ResolvedAtUtc = resolvedAtUtc,
            }
        );
    }

    private static string? Text(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
    }
}
