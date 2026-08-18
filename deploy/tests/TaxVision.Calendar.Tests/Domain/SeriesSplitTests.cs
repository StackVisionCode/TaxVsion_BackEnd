using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

/// <summary>
/// «Esta y las siguientes» — la operación donde una implementación ingenua corrompe el historial.
/// </summary>
public sealed class SeriesSplitTests
{
    private const string NewYork = "America/New_York";

    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly Guid Attendee = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    private static EventTiming NineAmFrom(DateOnly start) =>
        EventTiming.RecurringOf(start, new TimeOnly(9, 0), TimeSpan.FromHours(1), NewYork).Value;

    private static EventTiming TenAmFrom(DateOnly start) =>
        EventTiming.RecurringOf(start, new TimeOnly(10, 0), TimeSpan.FromHours(1), NewYork).Value;

    private static Appointment SeriesWithFebruaryCancellation()
    {
        var timing = NineAmFrom(new DateOnly(2026, 1, 5));
        var series = Appointment
            .Schedule(
                Guid.NewGuid(),
                AppointmentTitle.Create("Revision semanal").Value,
                timing,
                Guid.NewGuid(),
                Organizer,
                Now
            )
            .Value;

        series.MakeRecurring(RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value, timing, Organizer);
        series.AddAttendee(
            AttendeeKind.InternalUser,
            Attendee,
            null,
            AttendeeSnapshot.Create("Ana Preparadora", "ana@firma.test").Value,
            isRequired: true,
            Organizer,
            Now
        );
        series.LinkMeeting(Guid.NewGuid(), "abc-defg-hij");

        // Lunes 16 de febrero, cancelada. Es la que tiene que sobrevivir en la serie vieja.
        Assert.True(series.CancelOccurrence(Utc(2026, 2, 16, 14), Organizer, Now).IsSuccess);

        return series;
    }

    [Fact]
    public void Splitting_in_march_ends_the_old_series_and_starts_a_new_one()
    {
        var original = SeriesWithFebruaryCancellation();
        var cut = Utc(2026, 3, 9, 13); // lunes 9 de marzo, ya en horario de verano

        var follower = original.SplitForFollowing(
            cut,
            TenAmFrom(new DateOnly(2026, 3, 9)),
            RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
            Organizer,
            Now
        );

        Assert.True(follower.IsSuccess);

        // La vieja termina antes del corte y no produce nada desde el 9 de marzo.
        Assert.Contains("UNTIL=", original.Recurrence!.Value);
        var afterCut = OccurrenceExpander.Expand(original, cut, Utc(2026, 6, 1)).Value;
        Assert.Empty(afterCut);

        // Y sigue produciendo lo de antes.
        var beforeCut = OccurrenceExpander.Expand(original, Utc(2026, 1, 1), cut).Value;
        Assert.NotEmpty(beforeCut);

        // La nueva arranca en el corte, a las 10.
        var followerOccurrences = OccurrenceExpander.Expand(follower.Value, cut, Utc(2026, 4, 1)).Value;
        Assert.NotEmpty(followerOccurrences);
        Assert.Equal(14, followerOccurrences[0].StartUtc.Hour); // 10:00 EDT = 14:00Z
    }

    [Fact]
    public void The_exception_from_before_the_cut_stays_in_the_old_series()
    {
        var original = SeriesWithFebruaryCancellation();

        var follower = original.SplitForFollowing(
            Utc(2026, 3, 9, 13),
            TenAmFrom(new DateOnly(2026, 3, 9)),
            RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
            Organizer,
            Now
        );

        // Heredarla revivirÍa una cancelación de una fecha que la serie nueva no produce.
        Assert.Single(original.Exceptions);
        Assert.Equal(Utc(2026, 2, 16, 14), original.Exceptions[0].OriginalStartUtc);
        Assert.Empty(follower.Value.Exceptions);
    }

    [Fact]
    public void The_exceptions_from_after_the_cut_are_discarded()
    {
        var original = SeriesWithFebruaryCancellation();
        original.CancelOccurrence(Utc(2026, 3, 23, 13), Organizer, Now);
        Assert.Equal(2, original.Exceptions.Count);

        original.SplitForFollowing(
            Utc(2026, 3, 9, 13),
            TenAmFrom(new DateOnly(2026, 3, 9)),
            RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
            Organizer,
            Now
        );

        // La del 23 de marzo pertenece a la mitad nueva, que ya no la produce a esa hora.
        Assert.Single(original.Exceptions);
        Assert.Equal(Utc(2026, 2, 16, 14), original.Exceptions[0].OriginalStartUtc);
    }

    [Fact]
    public void The_new_series_does_not_inherit_the_meeting_room()
    {
        var original = SeriesWithFebruaryCancellation();
        Assert.NotNull(original.MeetingId);

        var follower = original
            .SplitForFollowing(
                Utc(2026, 3, 9, 13),
                TenAmFrom(new DateOnly(2026, 3, 9)),
                RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
                Organizer,
                Now
            )
            .Value;

        // Es otra serie: necesita su propia sala, y Communication la crea al recibir el evento.
        Assert.Null(follower.MeetingId);
        Assert.Null(follower.MeetingShortCode);
    }

    [Fact]
    public void The_new_series_inherits_the_attendees_and_records_where_it_came_from()
    {
        var original = SeriesWithFebruaryCancellation();

        var follower = original
            .SplitForFollowing(
                Utc(2026, 3, 9, 13),
                TenAmFrom(new DateOnly(2026, 3, 9)),
                RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
                Organizer,
                Now
            )
            .Value;

        Assert.Equal(original.Id, follower.SplitFromSeriesId);
        Assert.Single(follower.Attendees);
        Assert.Equal("Ana Preparadora", follower.Attendees[0].Snapshot.DisplayName);

        // Copias, no la misma instancia: son entidades de otro agregado.
        Assert.NotEqual(original.Attendees[0].Id, follower.Attendees[0].Id);
    }

    [Fact]
    public void Splitting_on_the_first_occurrence_is_refused()
    {
        var original = SeriesWithFebruaryCancellation();
        var first = Utc(2026, 1, 5, 14); // lunes 5 de enero, la primera

        var result = original.SplitForFollowing(
            first,
            TenAmFrom(new DateOnly(2026, 1, 5)),
            RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
            Organizer,
            Now
        );

        // Partir ahí dejaría la serie vieja sin ninguna ocurrencia. Para eso está EditEntireSeries.
        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Appointment.SplitOnFirstOccurrence", result.Error.Code);
        Assert.DoesNotContain("UNTIL=", original.Recurrence!.Value);
    }

    [Fact]
    public void Splitting_on_an_instant_that_is_not_an_occurrence_is_refused()
    {
        var original = SeriesWithFebruaryCancellation();

        var result = original.SplitForFollowing(
            Utc(2026, 3, 11, 13), // un miércoles
            TenAmFrom(new DateOnly(2026, 3, 11)),
            RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
            Organizer,
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Exception.NotAnOccurrence", result.Error.Code);
    }

    [Fact]
    public void Only_the_organizer_splits()
    {
        var original = SeriesWithFebruaryCancellation();

        var result = original.SplitForFollowing(
            Utc(2026, 3, 9, 13),
            TenAmFrom(new DateOnly(2026, 3, 9)),
            RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
            Attendee,
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Appointment.NotTheOrganizer", result.Error.Code);
    }

    [Fact]
    public void Editing_the_entire_series_keeps_the_existing_exceptions()
    {
        var original = SeriesWithFebruaryCancellation();

        var result = original.EditEntireSeries(
            TenAmFrom(new DateOnly(2026, 1, 5)),
            RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
            Organizer,
            Now
        );

        // Siguen apuntando a las mismas ocurrencias por su OriginalStartUtc, que no cambia.
        Assert.True(result.IsSuccess);
        Assert.Single(original.Exceptions);
    }

    [Fact]
    public void The_until_of_the_old_series_is_written_in_utc()
    {
        var original = SeriesWithFebruaryCancellation();

        original.SplitForFollowing(
            Utc(2026, 3, 9, 13),
            TenAmFrom(new DateOnly(2026, 3, 9)),
            RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
            Organizer,
            Now
        );

        // RFC 5545 lo exige: es la única parte de la regla del DST que es absoluta.
        Assert.Contains("UNTIL=20260309T125959Z", original.Recurrence!.Value);
    }

    [Fact]
    public void Setting_until_removes_a_count_that_was_there()
    {
        // El RFC prohíbe UNTIL y COUNT a la vez; dejarlos convierte el RRULE en texto que cada parser
        // interpreta a su manera.
        var rule = RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO;COUNT=20").Value;

        var limited = rule.EndingAt(Utc(2026, 3, 9, 12));

        Assert.True(limited.IsSuccess);
        Assert.DoesNotContain("COUNT=", limited.Value.Value);
        Assert.Contains("UNTIL=", limited.Value.Value);
    }

    [Fact]
    public void An_until_that_is_not_utc_is_rejected()
    {
        var rule = RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value;

        var limited = rule.EndingAt(new DateTime(2026, 3, 9, 12, 0, 0, DateTimeKind.Unspecified));

        Assert.True(limited.IsFailure);
        Assert.Equal("Calendar.Recurrence.UntilNotUtc", limited.Error.Code);
    }

    [Theory]
    [InlineData("todos los lunes")]
    [InlineData("BYDAY=MO")]
    [InlineData("")]
    [InlineData(null)]
    public void An_invalid_recurrence_rule_is_rejected(string? rule)
    {
        // "BYDAY=MO" sin FREQ se construiría con la primera frecuencia del enum en vez de fallar.
        Assert.True(RecurrenceRule.Create(rule).IsFailure);
    }
}
