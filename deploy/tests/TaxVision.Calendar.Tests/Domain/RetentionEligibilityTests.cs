using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

/// <summary>
/// La regla que decide qué puede borrar la retención. El caso que importa es el que no se borra: una
/// serie sin fin no tiene última ocurrencia, así que llamarla vieja por su fecha de creación borraría
/// la reunión semanal que el despacho tiene desde hace ocho años y sigue teniendo.
/// </summary>
public sealed class RetentionEligibilityTests
{
    private const string NewYork = "America/New_York";

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private static readonly DateTime Created = new(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Cutoff = new(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_endless_series_never_has_an_end_to_measure()
    {
        var series = Series("FREQ=WEEKLY;BYDAY=MO");

        Assert.False(series.Recurrence!.HasEnd);
        Assert.NotEmpty(OccurrenceExpander.Expand(series, Cutoff, Cutoff.AddYears(1)).Value);
    }

    [Theory]
    [InlineData("FREQ=WEEKLY;BYDAY=MO;COUNT=10")]
    [InlineData("FREQ=WEEKLY;BYDAY=MO;UNTIL=20160101T000000Z")]
    public void A_series_that_ends_runs_out_before_the_cutoff(string rule)
    {
        var series = Series(rule);

        Assert.True(series.Recurrence!.HasEnd);

        // Ni una ocurrencia desde el corte hasta dentro de un siglo: la serie terminó.
        var remaining = OccurrenceExpander.Expand(series, Cutoff, Cutoff.AddYears(100));
        Assert.True(remaining.IsSuccess);
        Assert.Empty(remaining.Value);
    }

    /// <summary>Una serie con fin que todavía no llegó sigue viva, aunque naciera hace años.</summary>
    [Fact]
    public void A_series_that_ends_in_the_future_is_still_alive()
    {
        var series = Series("FREQ=WEEKLY;BYDAY=MO;UNTIL=20300101T000000Z");

        Assert.NotEmpty(OccurrenceExpander.Expand(series, Cutoff, Cutoff.AddYears(100)).Value);
    }

    private Appointment Series(string rule)
    {
        var timing = EventTiming
            .RecurringOf(new DateOnly(2015, 1, 5), new TimeOnly(9, 0), TimeSpan.FromHours(1), NewYork)
            .Value;

        var series = Appointment
            .Schedule(
                _tenant,
                AppointmentTitle.Create("Reunion semanal").Value,
                timing,
                Guid.NewGuid(),
                _organizer,
                Created
            )
            .Value;

        series.MakeRecurring(RecurrenceRule.Create(rule).Value, timing, _organizer);
        return series;
    }
}
