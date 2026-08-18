using BuildingBlocks.TimeZones;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

public sealed class OccurrenceExpanderTests
{
    private const string NewYork = "America/New_York";
    private const string SantoDomingo = "America/Santo_Domingo";

    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    /// <summary>Serie «lunes 9:00» en la zona pedida, arrancando el lunes 5 de enero de 2026.</summary>
    private static Appointment WeeklyMondayNineAm(string timeZoneId = NewYork, string rule = "FREQ=WEEKLY;BYDAY=MO")
    {
        var timing = EventTiming
            .RecurringOf(new DateOnly(2026, 1, 5), new TimeOnly(9, 0), TimeSpan.FromHours(1), timeZoneId)
            .Value;

        var appointment = Appointment
            .Schedule(
                Guid.NewGuid(),
                AppointmentTitle.Create("Revision semanal").Value,
                timing,
                Guid.NewGuid(),
                Organizer,
                Now
            )
            .Value;

        Assert.True(appointment.MakeRecurring(RecurrenceRule.Create(rule).Value, timing, Organizer).IsSuccess);
        return appointment;
    }

    private static DateTime ExpectedByHand(string zoneId, int y, int m, int d, int hour)
    {
        Assert.True(IanaTimeZone.TryFindTimeZone(zoneId, out var zone));
        return TimeZoneInfo.ConvertTimeToUtc(new DateTime(y, m, d, hour, 0, 0, DateTimeKind.Unspecified), zone);
    }

    [Fact]
    public void A_series_crossing_the_dst_change_keeps_nine_am_local_and_shifts_its_utc()
    {
        var series = WeeklyMondayNineAm();

        var occurrences = OccurrenceExpander.Expand(series, Utc(2026, 1, 1), Utc(2026, 12, 31)).Value;

        // Enero está en horario estándar (14:00Z) y julio en horario de verano (13:00Z). Los valores
        // esperados se calculan aparte, con TimeZoneInfo, no con la propia implementación.
        var january = Find(occurrences, Utc(2026, 1, 12, 14));
        var july = Find(occurrences, Utc(2026, 7, 13, 13));

        Assert.Equal(ExpectedByHand(NewYork, 2026, 1, 12, 9), january.StartUtc);
        Assert.Equal(ExpectedByHand(NewYork, 2026, 7, 13, 9), july.StartUtc);
        Assert.NotEqual(january.StartUtc.TimeOfDay, july.StartUtc.TimeOfDay);
    }

    [Fact]
    public void A_series_in_a_zone_without_dst_keeps_the_same_utc_all_year()
    {
        var series = WeeklyMondayNineAm(SantoDomingo);

        var occurrences = OccurrenceExpander.Expand(series, Utc(2026, 1, 1), Utc(2026, 12, 31)).Value;

        var january = Find(occurrences, Utc(2026, 1, 12, 13));
        var july = Find(occurrences, Utc(2026, 7, 13, 13));

        Assert.Equal(january.StartUtc.TimeOfDay, july.StartUtc.TimeOfDay);
    }

    [Fact]
    public void A_cancelled_occurrence_disappears_and_the_rest_stay()
    {
        var series = WeeklyMondayNineAm();
        var target = Utc(2026, 1, 19, 14);

        Assert.True(series.CancelOccurrence(target, Organizer, Now).IsSuccess);

        var occurrences = OccurrenceExpander.Expand(series, Utc(2026, 1, 1), Utc(2026, 2, 1)).Value;

        Assert.DoesNotContain(occurrences, o => o.OriginalStartUtc == target);
        Assert.Contains(occurrences, o => o.OriginalStartUtc == Utc(2026, 1, 12, 14));
        Assert.Contains(occurrences, o => o.OriginalStartUtc == Utc(2026, 1, 26, 14));
    }

    [Fact]
    public void An_overridden_occurrence_is_returned_moved_and_keeps_its_original_start()
    {
        var series = WeeklyMondayNineAm();
        var original = Utc(2026, 1, 19, 14);
        var moved = Utc(2026, 1, 20, 16);

        Assert.True(
            series
                .OverrideOccurrence(original, moved, moved.AddHours(1), "Revision movida", null, Organizer, Now)
                .IsSuccess
        );

        var occurrences = OccurrenceExpander.Expand(series, Utc(2026, 1, 1), Utc(2026, 2, 1)).Value;
        var occurrence = Find(occurrences, moved, byEffectiveStart: true);

        // La identidad es el inicio ORIGINAL: es lo que la vuelve a encontrar cuando se la edite otra vez.
        Assert.Equal(original, occurrence.OriginalStartUtc);
        Assert.Equal(moved, occurrence.StartUtc);
        Assert.True(occurrence.IsException);
        Assert.Equal("Revision movida", occurrence.Title);
    }

    [Fact]
    public void An_occurrence_moved_backwards_from_outside_the_range_still_shows_up()
    {
        // El preparador adelanta la cita del 16 de marzo al 24 de febrero: tiene que aparecer en el
        // calendario de febrero, aunque el recorrido de la regla se detenga al final de febrero.
        var series = WeeklyMondayNineAm();
        var original = Utc(2026, 3, 16, 13);
        var moved = Utc(2026, 2, 24, 14);

        Assert.True(
            series.OverrideOccurrence(original, moved, moved.AddHours(1), null, null, Organizer, Now).IsSuccess
        );

        var february = OccurrenceExpander.Expand(series, Utc(2026, 2, 1), Utc(2026, 3, 1)).Value;

        Assert.Contains(february, o => o.OriginalStartUtc == original && o.StartUtc == moved);
    }

    [Fact]
    public void An_instant_the_series_never_produces_is_rejected()
    {
        var series = WeeklyMondayNineAm();

        // Un miércoles: la serie es de lunes.
        var result = series.CancelOccurrence(Utc(2026, 1, 14, 14), Organizer, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Exception.NotAnOccurrence", result.Error.Code);
        Assert.Empty(series.Exceptions);
    }

    [Fact]
    public void The_same_occurrence_cannot_have_two_exceptions()
    {
        var series = WeeklyMondayNineAm();
        var target = Utc(2026, 1, 19, 14);
        series.CancelOccurrence(target, Organizer, Now);

        var second = series.OverrideOccurrence(target, target.AddHours(2), null, null, null, Organizer, Now);

        Assert.True(second.IsFailure);
        Assert.Equal("Calendar.Exception.Duplicate", second.Error.Code);
        Assert.Single(series.Exceptions);
    }

    [Fact]
    public void An_override_that_changes_nothing_is_rejected()
    {
        var series = WeeklyMondayNineAm();

        var result = series.OverrideOccurrence(Utc(2026, 1, 19, 14), null, null, null, null, Organizer, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Exception.EmptyOverride", result.Error.Code);
    }

    [Fact]
    public void A_one_off_appointment_cannot_have_occurrence_exceptions()
    {
        var timing = EventTiming.PointInTimeOf(Utc(2026, 3, 10, 14), Utc(2026, 3, 10, 15), NewYork).Value;
        var appointment = Appointment
            .Schedule(Guid.NewGuid(), AppointmentTitle.Create("Puntual").Value, timing, Guid.NewGuid(), Organizer, Now)
            .Value;

        var result = appointment.CancelOccurrence(Utc(2026, 3, 10, 14), Organizer, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Exception.NotRecurring", result.Error.Code);
    }

    [Fact]
    public void A_three_year_series_queried_for_one_month_returns_only_that_month()
    {
        var series = WeeklyMondayNineAm();

        var june = OccurrenceExpander.Expand(series, Utc(2026, 6, 1), Utc(2026, 7, 1)).Value;

        Assert.Equal(5, june.Count); // los 5 lunes de junio de 2026
        foreach (var occurrence in june)
            Assert.Equal(6, occurrence.StartUtc.Month);
    }

    [Fact]
    public void A_range_that_would_produce_more_than_a_thousand_occurrences_fails_explicitly()
    {
        var timing = EventTiming
            .RecurringOf(new DateOnly(2026, 1, 1), new TimeOnly(9, 0), TimeSpan.FromMinutes(30), NewYork)
            .Value;
        var series = Appointment
            .Schedule(Guid.NewGuid(), AppointmentTitle.Create("Diaria").Value, timing, Guid.NewGuid(), Organizer, Now)
            .Value;
        series.MakeRecurring(RecurrenceRule.Create("FREQ=DAILY").Value, timing, Organizer);

        // Cuatro años de una serie diaria son ~1460 ocurrencias.
        var result = OccurrenceExpander.Expand(series, Utc(2026, 1, 1), Utc(2030, 1, 1));

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Occurrences.RangeTooLarge", result.Error.Code);
    }

    [Fact]
    public void An_inverted_range_is_rejected_instead_of_returning_nothing()
    {
        var series = WeeklyMondayNineAm();

        var result = OccurrenceExpander.Expand(series, Utc(2026, 6, 1), Utc(2026, 5, 1));

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Occurrences.RangeInverted", result.Error.Code);
    }

    [Fact]
    public void A_series_that_ends_stops_producing_occurrences()
    {
        var series = WeeklyMondayNineAm(rule: "FREQ=WEEKLY;BYDAY=MO;COUNT=3");

        var occurrences = OccurrenceExpander.Expand(series, Utc(2026, 1, 1), Utc(2026, 12, 31)).Value;

        Assert.Equal(3, occurrences.Count);
    }

    private static Occurrence Find(IReadOnlyList<Occurrence> occurrences, DateTime at, bool byEffectiveStart = false)
    {
        foreach (var occurrence in occurrences)
        {
            if ((byEffectiveStart ? occurrence.StartUtc : occurrence.OriginalStartUtc) == at)
                return occurrence;
        }

        Assert.Fail(
            $"No occurrence at {at:O}. Got: {string.Join(", ", occurrences.Select(o => o.StartUtc.ToString("O")))}"
        );
        return null!;
    }
}
