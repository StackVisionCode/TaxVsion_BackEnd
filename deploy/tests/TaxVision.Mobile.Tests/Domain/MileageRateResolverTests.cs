using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Tests.Domain;

public sealed class MileageRateResolverTests
{
    private static readonly IReadOnlyList<RatePeriod> NoOfficeRates = [];
    private static readonly IReadOnlyList<RatePeriod> Standard = IrsStandardMileageRates.All;

    [Fact]
    public void The_rate_saved_on_the_trip_wins()
    {
        var office = new[] { new RatePeriod("business", new DateOnly(2026, 1, 1), 0.50m, "USD") };

        var resolved = MileageRateResolver.Resolve(
            0.61m,
            "USD",
            "business",
            new DateOnly(2026, 3, 1),
            office,
            Standard
        );

        Assert.Equal(new ResolvedRate(0.61m, "USD", RateSource.Trip), resolved);
    }

    [Fact]
    public void Without_a_saved_rate_the_office_rate_in_force_is_used()
    {
        var office = new[]
        {
            new RatePeriod("business", new DateOnly(2026, 1, 1), 0.50m, "USD"),
            new RatePeriod("business", new DateOnly(2026, 4, 1), 0.55m, "USD"),
        };

        Assert.Equal(
            new ResolvedRate(0.50m, "USD", RateSource.Office),
            MileageRateResolver.Resolve(null, null, "business", new DateOnly(2026, 3, 31), office, Standard)
        );
        Assert.Equal(
            new ResolvedRate(0.55m, "USD", RateSource.Office),
            MileageRateResolver.Resolve(null, null, "business", new DateOnly(2026, 4, 1), office, Standard)
        );
    }

    [Fact]
    public void Without_office_rates_the_irs_rate_of_that_date_is_used_including_the_mid_year_change()
    {
        Assert.Equal(
            new ResolvedRate(0.725m, "USD", RateSource.Standard),
            MileageRateResolver.Resolve(null, null, "business", new DateOnly(2026, 6, 30), NoOfficeRates, Standard)
        );
        Assert.Equal(
            new ResolvedRate(0.76m, "USD", RateSource.Standard),
            MileageRateResolver.Resolve(null, null, "business", new DateOnly(2026, 7, 1), NoOfficeRates, Standard)
        );
        Assert.Equal(
            new ResolvedRate(0.70m, "USD", RateSource.Standard),
            MileageRateResolver.Resolve(null, null, "business", new DateOnly(2025, 12, 31), NoOfficeRates, Standard)
        );
    }

    [Fact]
    public void Personal_trips_and_dates_before_the_catalog_have_no_rate()
    {
        Assert.Null(
            MileageRateResolver.Resolve(null, null, "personal", new DateOnly(2026, 3, 1), NoOfficeRates, Standard)
        );
        Assert.Null(
            MileageRateResolver.Resolve(null, null, "business", new DateOnly(2019, 12, 31), NoOfficeRates, Standard)
        );
    }

    [Fact]
    public void The_date_that_decides_the_rate_is_the_office_local_date()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        // 1 de julio a las 02:00 UTC es todavía 30 de junio en Nueva York.
        Assert.Equal(
            new DateOnly(2026, 6, 30),
            MileageRateResolver.LocalDate(new DateTime(2026, 7, 1, 2, 0, 0, DateTimeKind.Utc), newYork)
        );
        // 1 de enero a las 03:00 UTC es todavía 31 de diciembre en Nueva York.
        Assert.Equal(
            new DateOnly(2025, 12, 31),
            MileageRateResolver.LocalDate(new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc), newYork)
        );
    }

    [Fact]
    public void The_estimate_rounds_to_cents_like_the_trip()
    {
        var rate = new ResolvedRate(0.725m, "USD", RateSource.Standard);

        Assert.Equal(7.25m, rate.EstimateFor(10 * 1609.344));
        Assert.Equal(0.73m, rate.EstimateFor(1609.344));
    }

    [Fact]
    public void The_irs_catalog_matches_irs_gov_and_moving_uses_the_medical_rate()
    {
        decimal? Rate(string purpose, int year, int month, int day) =>
            Standard
                .Where(p => p.Purpose == purpose && p.EffectiveFrom <= new DateOnly(year, month, day))
                .OrderByDescending(p => p.EffectiveFrom)
                .Select(p => (decimal?)p.RatePerMile)
                .FirstOrDefault();

        Assert.Equal(0.575m, Rate("business", 2020, 6, 1));
        Assert.Equal(0.585m, Rate("business", 2022, 6, 30));
        Assert.Equal(0.625m, Rate("business", 2022, 7, 1));
        Assert.Equal(0.67m, Rate("business", 2024, 6, 1));
        Assert.Equal(0.14m, Rate("charity", 2026, 8, 1));
        Assert.Equal(0.205m, Rate("medical", 2026, 2, 1));
        Assert.Equal(0.235m, Rate("medical", 2026, 8, 1));
        foreach (var medical in Standard.Where(p => p.Purpose == "medical"))
            Assert.Contains(
                Standard,
                p =>
                    p.Purpose == "moving"
                    && p.EffectiveFrom == medical.EffectiveFrom
                    && p.RatePerMile == medical.RatePerMile
            );
        Assert.DoesNotContain(Standard, p => p.Purpose == "personal");
        Assert.All(Standard, p => Assert.Equal("USD", p.Currency));
    }

    [Fact]
    public void A_personal_trip_has_no_rate_even_if_one_was_saved_or_the_office_has_one()
    {
        var office = new[] { new RatePeriod("personal", new DateOnly(2026, 1, 1), 0.30m, "USD") };

        Assert.Null(MileageRateResolver.Resolve(null, null, "personal", new DateOnly(2026, 3, 1), office, Standard));
        Assert.Null(MileageRateResolver.Resolve(0.30m, "USD", "personal", new DateOnly(2026, 3, 1), office, Standard));
    }
}
