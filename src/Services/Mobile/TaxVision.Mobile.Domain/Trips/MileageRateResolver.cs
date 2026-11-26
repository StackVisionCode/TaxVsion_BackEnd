namespace TaxVision.Mobile.Domain.Trips;

/// <summary>Tarifa por milla vigente desde una fecha, para un propósito.</summary>
public sealed record RatePeriod(string Purpose, DateOnly EffectiveFrom, decimal RatePerMile, string Currency);

/// <summary>De dónde salió la tarifa de un viaje.</summary>
public enum RateSource
{
    Trip,
    Office,
    Standard,
}

public sealed record ResolvedRate(decimal RatePerMile, string Currency, RateSource Source)
{
    private const double MetersPerMile = 1609.344;

    /// <summary>Estimado en dinero, redondeado como lo guarda el viaje al completarse.</summary>
    public decimal EstimateFor(double distanceMeters) =>
        decimal.Round((decimal)(distanceMeters / MetersPerMile) * RatePerMile, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Elige la tarifa de un viaje: la guardada en el viaje, si no la de la oficina vigente y, si tampoco, la del
/// IRS. La fecha que decide es la local de la oficina. Sin tarifa aplicable devuelve null (p. ej. personal).
/// </summary>
public static class MileageRateResolver
{
    private const string PersonalPurpose = "personal";

    public static ResolvedRate? Resolve(
        decimal? tripRatePerMile,
        string? tripCurrency,
        string purpose,
        DateOnly localDate,
        IReadOnlyList<RatePeriod> officeRates,
        IReadOnlyList<RatePeriod> standardRates
    )
    {
        // Un viaje personal no es deducible: nunca lleva tarifa, aunque la oficina haya creado una.
        if (purpose == PersonalPurpose)
            return null;
        if (tripRatePerMile.HasValue && !string.IsNullOrEmpty(tripCurrency))
            return new(tripRatePerMile.Value, tripCurrency, RateSource.Trip);
        if (InForce(officeRates, purpose, localDate) is { } office)
            return new(office.RatePerMile, office.Currency, RateSource.Office);
        if (InForce(standardRates, purpose, localDate) is { } standard)
            return new(standard.RatePerMile, standard.Currency, RateSource.Standard);
        return null;
    }

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

    private static RatePeriod? InForce(IReadOnlyList<RatePeriod> rates, string purpose, DateOnly date)
    {
        RatePeriod? best = null;
        foreach (var rate in rates)
        {
            if (rate.Purpose != purpose || rate.EffectiveFrom > date)
                continue;
            if (best is null || rate.EffectiveFrom > best.EffectiveFrom)
                best = rate;
        }
        return best;
    }
}
