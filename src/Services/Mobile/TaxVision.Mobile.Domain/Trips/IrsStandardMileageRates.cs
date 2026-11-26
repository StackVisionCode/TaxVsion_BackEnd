namespace TaxVision.Mobile.Domain.Trips;

/// <summary>
/// Tasas estándar del IRS en USD por milla, verificadas en
/// https://www.irs.gov/tax-professionals/standard-mileage-rates el 2026-11-25. La de mudanza es la misma que la
/// médica (solo militares, según el IRS). Personal no tiene tasa. Una tasa nueva entra con otra migración.
/// </summary>
public static class IrsStandardMileageRates
{
    public const string Source = "IRS standard mileage rates";

    public static readonly IReadOnlyList<RatePeriod> All = Build(
        // (desde, negocio, caridad, médico y mudanza)
        (new DateOnly(2020, 1, 1), 0.575m, 0.14m, 0.17m),
        (new DateOnly(2021, 1, 1), 0.56m, 0.14m, 0.16m),
        (new DateOnly(2022, 1, 1), 0.585m, 0.14m, 0.18m),
        (new DateOnly(2022, 7, 1), 0.625m, 0.14m, 0.22m),
        (new DateOnly(2023, 1, 1), 0.655m, 0.14m, 0.22m),
        (new DateOnly(2024, 1, 1), 0.67m, 0.14m, 0.21m),
        (new DateOnly(2025, 1, 1), 0.70m, 0.14m, 0.21m),
        (new DateOnly(2026, 1, 1), 0.725m, 0.14m, 0.205m),
        (new DateOnly(2026, 7, 1), 0.76m, 0.14m, 0.235m)
    );

    private static IReadOnlyList<RatePeriod> Build(
        params (DateOnly From, decimal Business, decimal Charity, decimal MedicalAndMoving)[] periods
    ) =>
        periods
            .SelectMany(p =>
                new[]
                {
                    new RatePeriod("business", p.From, p.Business, "USD"),
                    new RatePeriod("charity", p.From, p.Charity, "USD"),
                    new RatePeriod("medical", p.From, p.MedicalAndMoving, "USD"),
                    new RatePeriod("moving", p.From, p.MedicalAndMoving, "USD"),
                }
            )
            .ToArray();
}
