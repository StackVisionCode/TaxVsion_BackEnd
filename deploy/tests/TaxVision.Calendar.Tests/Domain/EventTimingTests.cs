using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

public sealed class EventTimingTests
{
    private const string NewYork = "America/New_York";

    [Fact]
    public void A_recurring_event_is_stored_as_wall_clock_and_never_as_utc()
    {
        var timing = EventTiming.RecurringOf(
            new DateOnly(2026, 1, 5),
            new TimeOnly(9, 0),
            TimeSpan.FromHours(1),
            NewYork
        );

        Assert.True(timing.IsSuccess);
        Assert.Null(timing.Value.StartUtc);
        Assert.Equal(new TimeOnly(9, 0), timing.Value.LocalStartTime);
        Assert.True(timing.Value.IsStoredAsWallClock);
    }

    [Fact]
    public void A_point_in_time_event_rejects_a_start_that_is_not_utc()
    {
        // El caso real no es este: es releer de la BD. EF devuelve datetime2 como Unspecified, asi
        // que sin los convertidores del DbContext una cita valida se rechaza a si misma al volver.
        var local = new DateTime(2026, 3, 9, 9, 0, 0, DateTimeKind.Unspecified);

        var timing = EventTiming.PointInTimeOf(local, local.AddHours(1), NewYork);

        Assert.True(timing.IsFailure);
        Assert.Equal("Calendar.Timing.NotUtc", timing.Error.Code);
    }

    [Fact]
    public void An_all_day_event_keeps_its_date_when_read_from_a_negative_offset()
    {
        // El 4 de julio guardado como medianoche UTC lo ve el 3 quien esta en UTC-5. DateOnly no
        // tiene offset que aplicar, asi que no hay nada que correr.
        var timing = EventTiming.AllDayOf(new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 4), NewYork);

        Assert.True(timing.IsSuccess);
        Assert.Equal(new DateOnly(2026, 7, 4), timing.Value.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 4), timing.Value.EndDate);
        Assert.Null(timing.Value.StartUtc);
    }

    [Fact]
    public void An_all_day_event_of_a_single_day_is_valid()
    {
        // EndDate es inclusiva de cara al usuario: del 4 al 4 es un dia, no un rango invertido.
        var timing = EventTiming.AllDayOf(new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 4), NewYork);

        Assert.True(timing.IsSuccess);
    }

    [Fact]
    public void An_event_that_ends_before_it_starts_is_rejected()
    {
        var start = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc);

        var timing = EventTiming.PointInTimeOf(start, start.AddHours(-1), NewYork);

        Assert.True(timing.IsFailure);
        Assert.Equal("Calendar.Timing.EndBeforeStart", timing.Error.Code);
    }

    [Fact]
    public void An_event_longer_than_thirty_days_is_rejected()
    {
        var start = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc);

        var timing = EventTiming.PointInTimeOf(start, start.AddDays(31), NewYork);

        Assert.True(timing.IsFailure);
        Assert.Equal("Calendar.Timing.DurationTooLong", timing.Error.Code);
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    // EST resuelve a Bogota, UTC-5 SIN horario de verano: quien lo escribe pensando en Nueva York
    // recibe citas corridas medio ano. Por eso el dominio exige la forma Area/Location.
    [InlineData("EST")]
    [InlineData("MST")]
    [InlineData("HST")]
    [InlineData("")]
    [InlineData(null)]
    public void An_invalid_time_zone_is_rejected(string? timeZoneId)
    {
        var start = new DateTime(2026, 3, 9, 10, 0, 0, DateTimeKind.Utc);

        var timing = EventTiming.PointInTimeOf(start, start.AddHours(1), timeZoneId);

        Assert.True(timing.IsFailure);
        Assert.Equal("Calendar.Timing.InvalidTimeZone", timing.Error.Code);
    }
}
