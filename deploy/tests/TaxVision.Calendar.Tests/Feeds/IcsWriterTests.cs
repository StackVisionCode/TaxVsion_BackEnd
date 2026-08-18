using TaxVision.Calendar.Application.Feeds;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Feeds;

/// <summary>
/// Lo que hay que ver en el archivo es que las excepciones sobrevivan: una serie sin `EXDATE` muestra
/// en el calendario del usuario una reunión que se canceló, y sin `RECURRENCE-ID` la movida aparece
/// dos veces.
/// </summary>
public sealed class IcsWriterTests
{
    private const string NewYork = "America/New_York";

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_series_keeps_its_rule_its_cancelled_date_and_its_moved_occurrence()
    {
        var series = WeeklySeries();

        // 9 de marzo 9:00 en Nueva York = 13:00Z (ya en horario de verano).
        var cancelled = new DateTime(2026, 3, 9, 13, 0, 0, DateTimeKind.Utc);
        var moved = new DateTime(2026, 3, 16, 13, 0, 0, DateTimeKind.Utc);

        series.CancelOccurrence(cancelled, _organizer, Now);
        series.OverrideOccurrence(
            moved,
            new DateTime(2026, 3, 16, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 16, 19, 0, 0, DateTimeKind.Utc),
            newTitle: null,
            newLocation: null,
            _organizer,
            Now
        );

        var ics = IcsWriter.Write([series]);

        Assert.Contains("RRULE:FREQ=WEEKLY;BYDAY=MO", ics);
        Assert.Contains($"EXDATE;TZID={NewYork}:20260309T090000", ics);
        Assert.Contains($"RECURRENCE-ID;TZID={NewYork}:20260316T090000", ics);
        Assert.Contains($"DTSTART;TZID={NewYork}:20260316T140000", ics);

        // Una VEVENT por la serie y otra por la movida: expandir las 52 ocurrencias le quitaria al
        // cliente la nocion de que son una serie.
        Assert.Equal(2, Occurrences(ics, "BEGIN:VEVENT"));
    }

    /// <summary>Sin VTIMEZONE, un cliente estricto no sabe qué significa el TZID y cae a hora local.</summary>
    [Fact]
    public void The_feed_declares_the_time_zone_it_uses()
    {
        var ics = IcsWriter.Write([WeeklySeries()]);

        Assert.Contains("BEGIN:VTIMEZONE", ics);
        Assert.Contains($"TZID:{NewYork}", ics);
    }

    [Fact]
    public void A_cancelled_appointment_is_not_in_the_feed()
    {
        var series = WeeklySeries();
        series.Cancel(_organizer, "ya no aplica", Now);

        Assert.Equal(0, Occurrences(IcsWriter.Write([series]), "BEGIN:VEVENT"));
    }

    private Appointment WeeklySeries()
    {
        var timing = EventTiming
            .RecurringOf(new DateOnly(2026, 3, 2), new TimeOnly(9, 0), TimeSpan.FromHours(1), NewYork)
            .Value;

        var series = Appointment
            .Schedule(
                _tenant,
                AppointmentTitle.Create("Revision semanal").Value,
                timing,
                Guid.NewGuid(),
                _organizer,
                Now
            )
            .Value;

        series.MakeRecurring(RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value, timing, _organizer);
        return series;
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
