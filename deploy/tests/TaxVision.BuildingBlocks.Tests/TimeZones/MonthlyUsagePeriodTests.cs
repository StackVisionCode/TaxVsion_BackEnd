using BuildingBlocks.TimeZones;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.TimeZones;

/// <summary>
/// P11: los cupos mensuales se reinician el día de alta de la suscripción, a medianoche de la oficina. Sin ancla
/// queda el mes UTC de siempre.
/// </summary>
public sealed class MonthlyUsagePeriodTests
{
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");

    // Alta el 15-mar a las 10:00 de Nueva York.
    private static readonly DateTime AnchorOn15th = new(2026, 3, 15, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Before_the_anchor_day_the_period_started_last_month()
    {
        var start = MonthlyUsagePeriod.StartUtc(
            new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc),
            AnchorOn15th,
            NewYork
        );

        // Medianoche del 15-oct en Nueva York (EDT).
        Assert.Equal(new DateTime(2026, 10, 15, 4, 0, 0, DateTimeKind.Utc), start);
    }

    [Fact]
    public void At_local_midnight_of_the_anchor_day_a_new_period_starts()
    {
        var midnight = new DateTime(2026, 11, 15, 5, 0, 0, DateTimeKind.Utc);

        Assert.Equal(midnight, MonthlyUsagePeriod.StartUtc(midnight, AnchorOn15th, NewYork));
        Assert.Equal(
            new DateTime(2026, 10, 15, 4, 0, 0, DateTimeKind.Utc),
            MonthlyUsagePeriod.StartUtc(midnight.AddSeconds(-1), AnchorOn15th, NewYork)
        );
    }

    [Fact]
    public void An_anchor_on_the_31st_uses_the_last_day_of_shorter_months()
    {
        var anchor = new DateTime(2027, 1, 31, 15, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            new DateTime(2027, 2, 28, 5, 0, 0, DateTimeKind.Utc),
            MonthlyUsagePeriod.StartUtc(new DateTime(2027, 2, 28, 12, 0, 0, DateTimeKind.Utc), anchor, NewYork)
        );
        Assert.Equal(
            new DateTime(2027, 1, 31, 5, 0, 0, DateTimeKind.Utc),
            MonthlyUsagePeriod.StartUtc(new DateTime(2027, 2, 27, 12, 0, 0, DateTimeKind.Utc), anchor, NewYork)
        );
    }

    [Fact]
    public void The_anchor_day_is_read_in_the_office_zone()
    {
        // 03:00Z del 1-mar es todavía el 28-feb en Nueva York.
        var anchor = new DateTime(2026, 3, 1, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            new DateTime(2026, 10, 28, 4, 0, 0, DateTimeKind.Utc),
            MonthlyUsagePeriod.StartUtc(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), anchor, NewYork)
        );
    }

    [Fact]
    public void Without_an_anchor_the_period_is_the_utc_month()
    {
        Assert.Equal(
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            MonthlyUsagePeriod.StartUtc(new DateTime(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc), null, NewYork)
        );
    }
}
