namespace TaxVision.Mobile.Domain.Trips;

/// <summary>
/// Fila del catálogo global de tasas estándar (no es de ninguna oficina). Se siembra por migración desde
/// <see cref="IrsStandardMileageRates"/>; el sistema solo la lee.
/// </summary>
public sealed class StandardMileageRate
{
    private StandardMileageRate() { }

    public Guid Id { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public DateOnly EffectiveFrom { get; private set; }
    public decimal RatePerMile { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;

    /// <summary>Id estable a partir de propósito y fecha, para que la siembra sea idempotente.</summary>
    public static StandardMileageRate From(RatePeriod period, string source) =>
        new()
        {
            Id = StableId(period.Purpose, period.EffectiveFrom),
            Purpose = period.Purpose,
            EffectiveFrom = period.EffectiveFrom,
            RatePerMile = period.RatePerMile,
            Currency = period.Currency,
            Source = source,
        };

    public RatePeriod ToPeriod() => new(Purpose, EffectiveFrom, RatePerMile, Currency);

    private static Guid StableId(string purpose, DateOnly from)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"irs-standard-rate:{purpose}:{from:yyyy-MM-dd}")
        );
        return new Guid(hash.AsSpan(0, 16));
    }
}
