using System.Globalization;
using BuildingBlocks.TimeZones;
using BuildingBlocks.TimeZones.Formatting;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.TimeZones;

/// <summary>Formato único aprobado (P15): "Wed, Nov 18, 2026, 9:00 AM · New York time, GMT-5".</summary>
public sealed class ZonedTimeFormatterTests
{
    private static readonly DateTime November18NineAmNewYork = Instant("2026-11-18T14:00:00Z");

    [Fact]
    public void Date_time_is_written_in_the_zone_with_its_city_and_gmt_offset()
    {
        var text = ZonedTimeFormatter.DateTime(November18NineAmNewYork, ResolvedTimeZone.Resolve("America/New_York"));

        Assert.Equal("Wed, Nov 18, 2026, 9:00 AM · New York time, GMT-5", text);
    }

    [Fact]
    public void Daylight_saving_changes_the_offset_in_the_label()
    {
        var text = ZonedTimeFormatter.DateTime(
            Instant("2026-07-01T13:00:00Z"),
            ResolvedTimeZone.Resolve("America/New_York")
        );

        Assert.Equal("Wed, Jul 1, 2026, 9:00 AM · New York time, GMT-4", text);
    }

    [Fact]
    public void Half_hour_offsets_keep_their_minutes()
    {
        var text = ZonedTimeFormatter.DateTime(
            Instant("2026-11-18T03:30:00Z"),
            ResolvedTimeZone.Resolve("Asia/Kolkata")
        );

        Assert.Equal("Wed, Nov 18, 2026, 9:00 AM · Kolkata time, GMT+5:30", text);
    }

    [Fact]
    public void An_office_in_utc_is_labelled_utc()
    {
        var text = ZonedTimeFormatter.DateTime(November18NineAmNewYork, ResolvedTimeZone.Utc);

        Assert.Equal("Wed, Nov 18, 2026, 2:00 PM · UTC", text);
    }

    [Fact]
    public void Date_of_an_instant_uses_the_day_in_the_zone()
    {
        // 01:30 UTC del 19-nov sigue siendo 18-nov en Nueva York.
        var text = ZonedTimeFormatter.Date(
            Instant("2026-11-19T01:30:00Z"),
            ResolvedTimeZone.Resolve("America/New_York")
        );

        Assert.Equal("Nov 18, 2026", text);
    }

    [Fact]
    public void A_civil_date_is_written_as_is_without_any_zone()
    {
        Assert.Equal("Nov 18, 2026", ZonedTimeFormatter.CivilDate(new DateOnly(2026, 11, 18)));
    }

    [Fact]
    public void Spanish_uses_the_spanish_pattern_and_label()
    {
        var text = ZonedTimeFormatter.DateTime(
            November18NineAmNewYork,
            ResolvedTimeZone.Resolve("America/New_York"),
            "es"
        );

        Assert.Equal("18 de noviembre de 2026, 9:00 · hora de New York, GMT-5", text);
        Assert.Equal("18 de noviembre de 2026", ZonedTimeFormatter.CivilDate(new DateOnly(2026, 11, 18), "es"));
    }

    [Fact]
    public void Output_does_not_depend_on_the_culture_of_the_process()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
            var text = ZonedTimeFormatter.DateTime(
                November18NineAmNewYork,
                ResolvedTimeZone.Resolve("America/New_York")
            );
            Assert.Equal("Wed, Nov 18, 2026, 9:00 AM · New York time, GMT-5", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static DateTime Instant(string utc) =>
        DateTime.Parse(
            utc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal
        );
}
