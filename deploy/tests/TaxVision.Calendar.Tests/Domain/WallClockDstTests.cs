using BuildingBlocks.TimeZones;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

/// <summary>
/// Los tests que deciden si este servicio es correcto. El valor esperado se calcula <b>aparte</b>,
/// con <see cref="TimeZoneInfo"/> a mano: comparar contra la propia implementacion no prueba nada.
/// La excepcion son los dos bordes del cambio de hora, donde <c>ConvertTimeToUtc</c> lanza o elige
/// distinto — ahi el test verifica la politica decidida, no la coincidencia entre motores.
/// </summary>
public sealed class WallClockDstTests
{
    private const string NewYork = "America/New_York";
    private const string SantoDomingo = "America/Santo_Domingo";

    private static CalendarTimeZone Zone(string id) => CalendarTimeZone.Create(id).Value;

    private static DateTime ExpectedByHand(string zoneId, DateOnly date, TimeOnly time)
    {
        Assert.True(IanaTimeZone.TryFindTimeZone(zoneId, out var zone));
        var wall = date.ToDateTime(time, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(wall, zone);
    }

    [Fact]
    public void A_weekly_nine_am_keeps_its_wall_clock_across_the_dst_change_and_its_utc_shifts_by_an_hour()
    {
        var nineAm = new TimeOnly(9, 0);
        var winter = new DateOnly(2026, 1, 12); // lunes, horario estandar
        var summer = new DateOnly(2026, 7, 13); // lunes, horario de verano

        var winterUtc = WallClock.ToUtc(winter, nineAm, Zone(NewYork));
        var summerUtc = WallClock.ToUtc(summer, nineAm, Zone(NewYork));

        Assert.True(winterUtc.IsSuccess);
        Assert.True(summerUtc.IsSuccess);

        // Las 9:00 de pared las dos veces, y por eso el instante UTC es distinto.
        Assert.Equal(ExpectedByHand(NewYork, winter, nineAm), winterUtc.Value);
        Assert.Equal(ExpectedByHand(NewYork, summer, nineAm), summerUtc.Value);
        Assert.Equal(14, winterUtc.Value.Hour);
        Assert.Equal(13, summerUtc.Value.Hour);

        // Si el recurrente se hubiera guardado en UTC, estas dos serian la misma hora y la reunion
        // se habria corrido una hora en enero.
        Assert.NotEqual(winterUtc.Value.TimeOfDay, summerUtc.Value.TimeOfDay);
    }

    [Fact]
    public void A_zone_without_dst_keeps_the_same_utc_all_year()
    {
        var nineAm = new TimeOnly(9, 0);
        var winter = new DateOnly(2026, 1, 12);
        var summer = new DateOnly(2026, 7, 13);

        var winterUtc = WallClock.ToUtc(winter, nineAm, Zone(SantoDomingo));
        var summerUtc = WallClock.ToUtc(summer, nineAm, Zone(SantoDomingo));

        Assert.Equal(ExpectedByHand(SantoDomingo, winter, nineAm), winterUtc.Value);
        Assert.Equal(winterUtc.Value.TimeOfDay, summerUtc.Value.TimeOfDay);
        Assert.Equal(13, winterUtc.Value.Hour);
    }

    [Fact]
    public void The_wall_clock_time_that_does_not_exist_is_rejected_when_creating()
    {
        // El 8 de marzo de 2026 el reloj de Nueva York salta de 2:00 a 3:00: las 2:30 no ocurren.
        // Correrla en silencio dejaria al usuario con una cita a una hora que no eligio.
        var result = WallClock.ToUtc(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), Zone(NewYork));

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Timing.InvalidLocalTime", result.Error.Code);
    }

    [Fact]
    public void The_wall_clock_time_that_does_not_exist_shifts_forward_when_expanding_a_series()
    {
        // Una serie sembrada a una hora valida no puede romperse un dia al ano: esa ocurrencia corre.
        var result = WallClock.ToUtcShiftingOverGaps(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), Zone(NewYork));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc), result.Value);
    }

    [Fact]
    public void The_ambiguous_wall_clock_time_resolves_to_the_first_of_the_two()
    {
        // El 1 de noviembre de 2026 la 1:30 ocurre dos veces en Nueva York. Gana la primera, que es
        // a la que llega el que mira el reloj de pared.
        var result = WallClock.ToUtc(new DateOnly(2026, 11, 1), new TimeOnly(1, 30), Zone(NewYork));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), result.Value);
    }

    [Fact]
    public void The_ambiguous_time_is_where_the_two_engines_disagree()
    {
        // Este test documenta por que hay un solo motor (ADR-C-13). TimeZoneInfo elige la SEGUNDA
        // ocurrencia y difiere una hora: si la expansion usara un motor y el StartUtc otro, el
        // recordatorio saldria tarde una vez al ano sin error en ningun log.
        var ours = WallClock.ToUtc(new DateOnly(2026, 11, 1), new TimeOnly(1, 30), Zone(NewYork)).Value;
        var theirs = ExpectedByHand(NewYork, new DateOnly(2026, 11, 1), new TimeOnly(1, 30));

        Assert.Equal(TimeSpan.FromHours(1), theirs - ours);
    }

    [Fact]
    public void Neither_dst_edge_throws()
    {
        var gap = Record.Exception(() => WallClock.ToUtc(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), Zone(NewYork)));
        var ambiguous = Record.Exception(() =>
            WallClock.ToUtc(new DateOnly(2026, 11, 1), new TimeOnly(1, 30), Zone(NewYork))
        );

        Assert.Null(gap);
        Assert.Null(ambiguous);
    }

    [Fact]
    public void A_round_trip_through_the_wall_clock_returns_the_same_local_time()
    {
        var date = new DateOnly(2026, 7, 13);
        var nineAm = new TimeOnly(9, 0);

        var utc = WallClock.ToUtc(date, nineAm, Zone(NewYork)).Value;
        var back = WallClock.ToWallClock(utc, Zone(NewYork));

        Assert.True(back.IsSuccess);
        Assert.Equal(date.ToDateTime(nineAm), back.Value);
    }
}
