using System.Globalization;
using System.Text.Json;
using BuildingBlocks.TimeZones;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.TimeZones;

/// <summary>
/// Vectores compartidos con Node (`deploy/tests/Shared/temporal-vectors.json`). Los valores salen de Intl (ICU);
/// acá se comprueba que `IanaTimeZone` + `TimeZoneInfo` dan lo mismo, en Windows y en Linux.
/// </summary>
public sealed class TemporalVectorsTests
{
    private static readonly JsonDocument Vectors = JsonDocument.Parse(
        ReadRepoFile("deploy/tests/Shared/temporal-vectors.json")
    );

    public static IEnumerable<object[]> UtcToLocal() =>
        Vectors
            .RootElement.GetProperty("utcToLocal")
            .EnumerateArray()
            .Select(v =>
                new object[]
                {
                    v.GetProperty("zone").GetString()!,
                    v.GetProperty("utc").GetString()!,
                    v.GetProperty("local").GetString()!,
                    v.GetProperty("civilDate").GetString()!,
                    v.GetProperty("offset").GetString()!,
                }
            );

    public static IEnumerable<object[]> CivilDayBounds() =>
        Vectors
            .RootElement.GetProperty("civilDayBounds")
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

    [Theory]
    [MemberData(nameof(UtcToLocal))]
    public void Utc_instant_converts_to_the_local_time_and_civil_date_of_the_zone(
        string zone,
        string utc,
        string local,
        string civilDate,
        string offset
    )
    {
        Assert.True(IanaTimeZone.TryFindTimeZone(zone, out var timeZone), $"{zone} must resolve.");
        var instant = DateTime.Parse(
            utc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal
        );

        var converted = TimeZoneInfo.ConvertTimeFromUtc(instant, timeZone);
        var actualOffset = timeZone.GetUtcOffset(instant);

        Assert.Equal(local, converted.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture));
        Assert.Equal(civilDate, DateOnly.FromDateTime(converted).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal(offset, FormatOffset(actualOffset));
    }

    [Theory]
    [MemberData(nameof(CivilDayBounds))]
    public void Civil_day_starts_and_ends_at_local_midnight_even_on_daylight_saving_days(
        string zone,
        string date,
        string startUtc,
        string endUtc
    )
    {
        Assert.True(IanaTimeZone.TryFindTimeZone(zone, out var timeZone), $"{zone} must resolve.");
        var day = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var start = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), timeZone);
        var end = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone).AddSeconds(-1);

        Assert.Equal(startUtc, start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        Assert.Equal(endUtc, end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }

    private static string FormatOffset(TimeSpan offset) =>
        $"{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' walking up from {AppContext.BaseDirectory}."
        );
    }
}
