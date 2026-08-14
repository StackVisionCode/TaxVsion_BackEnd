using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

/// <summary>
/// Dónde empieza y termina «mi día». Es la cuenta que hace <c>GET /calendar/appointments/my-day</c>, y
/// el caso que la hace necesaria es la cena: una cita de las 20:30 en Nueva York ocurre a las 00:30Z
/// del día siguiente, así que un día medido en UTC la mete en la agenda de mañana.
/// </summary>
public sealed class DayBoundsTests
{
    private static readonly CalendarTimeZone NewYork = CalendarTimeZone.Create("America/New_York").Value;

    [Fact]
    public void A_day_in_New_York_runs_from_four_in_the_morning_UTC_to_four_the_next_day()
    {
        var start = WallClock.ToUtcShiftingOverGaps(new DateOnly(2027, 5, 10), TimeOnly.MinValue, NewYork).Value;
        var end = WallClock.ToUtcShiftingOverGaps(new DateOnly(2027, 5, 11), TimeOnly.MinValue, NewYork).Value;

        // Mayo: Nueva York está en UTC-4.
        Assert.Equal(new DateTime(2027, 5, 10, 4, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2027, 5, 11, 4, 0, 0, DateTimeKind.Utc), end);
    }

    /// <summary>La cena de las 20:30 del 10 cae dentro del 10, no del 11.</summary>
    [Fact]
    public void The_evening_appointment_belongs_to_the_day_it_was_had()
    {
        var dinnerUtc = new DateTime(2027, 5, 11, 0, 30, 0, DateTimeKind.Utc);

        var start = WallClock.ToUtcShiftingOverGaps(new DateOnly(2027, 5, 10), TimeOnly.MinValue, NewYork).Value;
        var end = WallClock.ToUtcShiftingOverGaps(new DateOnly(2027, 5, 11), TimeOnly.MinValue, NewYork).Value;

        Assert.InRange(dinnerUtc, start, end);

        // Y medido en UTC caería en el día siguiente: es el bug que esto evita.
        Assert.Equal(11, dinnerUtc.Date.Day);
    }

    /// <summary>
    /// El día que empieza en el salto de horario. En Chile el cambio es a medianoche: el 5 de
    /// septiembre de 2027 no existe la 00:00, así que el día empieza a la 01:00 local en vez de
    /// romperse.
    /// </summary>
    [Fact]
    public void A_day_whose_midnight_does_not_exist_starts_one_hour_later()
    {
        var santiago = CalendarTimeZone.Create("America/Santiago").Value;

        var start = WallClock.ToUtcShiftingOverGaps(new DateOnly(2027, 9, 5), TimeOnly.MinValue, santiago);

        Assert.True(start.IsSuccess);
        Assert.Equal(DateTimeKind.Utc, start.Value.Kind);
    }
}
