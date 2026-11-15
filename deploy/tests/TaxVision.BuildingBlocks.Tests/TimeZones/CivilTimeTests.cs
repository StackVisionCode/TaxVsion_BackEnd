using System.Globalization;
using System.Text.Json;
using BuildingBlocks.TimeZones;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.TimeZones;

/// <summary>Cálculo civil puro de BuildingBlocks.TimeZones contra los vectores compartidos (`temporal-vectors.json`).</summary>
public sealed class CivilTimeTests
{
    private static readonly JsonElement Vectors = JsonDocument.Parse(TemporalVectorFile.Read()).RootElement;

    public static IEnumerable<object[]> UtcToLocal() =>
        Vectors
            .GetProperty("utcToLocal")
            .EnumerateArray()
            .Select(v =>
                new object[]
                {
                    v.GetProperty("zone").GetString()!,
                    v.GetProperty("utc").GetString()!,
                    v.GetProperty("civilDate").GetString()!,
                }
            );

    public static IEnumerable<object[]> DayBounds() =>
        Vectors
            .GetProperty("civilDayBounds")
            .EnumerateArray()
            .Select(v =>
                new object[]
                {
                    v.GetProperty("zone").GetString()!,
                    v.GetProperty("date").GetString()!,
                    v.GetProperty("startUtc").GetString()!,
                    v.GetProperty("endUtc").GetString()!,
                }
            );

    public static IEnumerable<object[]> WallToUtc() =>
        Vectors
            .GetProperty("wallToUtc")
            .EnumerateArray()
            .Select(v =>
                new object[]
                {
                    v.GetProperty("zone").GetString()!,
                    v.GetProperty("local").GetString()!,
                    v.GetProperty("kind").GetString()!,
                    v.TryGetProperty("utc", out var utc) ? utc.GetString()! : v.GetProperty("onExpandUtc").GetString()!,
                }
            );

    [Theory]
    [MemberData(nameof(UtcToLocal))]
    public void Civil_date_of_an_instant_is_the_day_in_the_zone(string zone, string utc, string civilDate)
    {
        Assert.Equal(Day(civilDate), CivilDate.Of(Instant(utc), ResolvedTimeZone.Resolve(zone)));
    }

    [Theory]
    [MemberData(nameof(DayBounds))]
    public void Local_day_starts_at_local_midnight_and_ends_one_second_before_the_next(
        string zone,
        string date,
        string startUtc,
        string endUtc
    )
    {
        var resolved = ResolvedTimeZone.Resolve(zone);

        Assert.Equal(Instant(startUtc), LocalDay.StartUtc(Day(date), resolved));
        Assert.Equal(Instant(endUtc), LocalDay.EndUtc(Day(date), resolved));
    }

    [Theory]
    [MemberData(nameof(WallToUtc))]
    public void Wall_time_converts_with_the_calendar_policy_when_expanding(
        string zone,
        string local,
        string kind,
        string utc
    )
    {
        var (date, time) = Wall(local);

        var result = WallTime.ToUtc(date, time, ResolvedTimeZone.Resolve(zone), NonexistentTimePolicy.ShiftForward);

        Assert.True(result.IsSuccess, kind);
        Assert.Equal(Instant(utc), result.Value);
        Assert.Equal(DateTimeKind.Utc, result.Value.Kind);
    }

    [Fact]
    public void A_nonexistent_wall_time_is_rejected_when_creating()
    {
        var (date, time) = Wall("2026-03-08T02:30");

        var result = WallTime.ToUtc(
            date,
            time,
            ResolvedTimeZone.Resolve("America/New_York"),
            NonexistentTimePolicy.Reject
        );

        Assert.True(result.IsFailure);
        Assert.Equal(TimeZoneErrors.NonexistentLocalTime.Code, result.Error.Code);
    }

    [Fact]
    public void An_ambiguous_wall_time_is_valid_when_creating_and_takes_the_first_occurrence()
    {
        var (date, time) = Wall("2026-11-01T01:30");

        var result = WallTime.ToUtc(
            date,
            time,
            ResolvedTimeZone.Resolve("America/New_York"),
            NonexistentTimePolicy.Reject
        );

        Assert.Equal(Instant("2026-11-01T05:30:00Z"), result.Value);
    }

    [Fact]
    public void Civil_days_between_count_calendar_days_in_the_zone_not_24_hour_blocks()
    {
        var newYork = ResolvedTimeZone.Resolve("America/New_York");

        // 20:30 del 12-nov a 08:00 del 13-nov en NY: un día civil aunque pasen menos de 24 h.
        Assert.Equal(1, CivilDays.Between(Instant("2026-11-13T01:30:00Z"), Instant("2026-11-13T13:00:00Z"), newYork));
        // Mismo par visto en UTC: el mismo día.
        Assert.Equal(
            0,
            CivilDays.Between(Instant("2026-11-13T01:30:00Z"), Instant("2026-11-13T13:00:00Z"), ResolvedTimeZone.Utc)
        );
        // Hacia atrás es negativo.
        Assert.Equal(-1, CivilDays.Between(Instant("2026-11-13T13:00:00Z"), Instant("2026-11-13T01:30:00Z"), newYork));
    }

    [Fact]
    public void A_missing_or_invalid_zone_resolves_to_utc_and_says_it_is_a_fallback()
    {
        var missing = ResolvedTimeZone.Resolve(null);
        var invalid = ResolvedTimeZone.Resolve("Not/AZone");
        var valid = ResolvedTimeZone.Resolve(" America/Chicago ");

        Assert.Equal(IanaTimeZone.UtcId, missing.Id);
        Assert.True(missing.IsFallback);
        Assert.True(invalid.IsFallback);
        Assert.Equal("America/Chicago", valid.Id);
        Assert.False(valid.IsFallback);
    }

    [Fact]
    public void An_unspecified_kind_instant_is_read_as_utc()
    {
        var unspecified = DateTime.SpecifyKind(new DateTime(2026, 11, 13, 1, 30, 0), DateTimeKind.Unspecified);

        Assert.Equal(
            new DateOnly(2026, 11, 12),
            CivilDate.Of(unspecified, ResolvedTimeZone.Resolve("America/New_York"))
        );
    }

    private static DateTime Instant(string utc) =>
        DateTime.Parse(
            utc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal
        );

    private static DateOnly Day(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static (DateOnly Date, TimeOnly Time) Wall(string local)
    {
        var parsed = DateTime.ParseExact(local, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        return (DateOnly.FromDateTime(parsed), TimeOnly.FromDateTime(parsed));
    }
}
