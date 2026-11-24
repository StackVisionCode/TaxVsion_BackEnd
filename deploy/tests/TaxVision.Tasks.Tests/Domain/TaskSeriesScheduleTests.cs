using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

/// <summary>
/// La serie siembra desde la última fecha que planificó, no desde el vencimiento que tenga la tarea; las series con plazo
/// legal no saltan obligaciones; "N meses después de completar" recorta al último día (D-15).
/// </summary>
public sealed class TaskSeriesScheduleTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly ResolvedTimeZone NewYork = ResolvedTimeZone.Resolve("America/New_York");

    private static DateTime Eod(int y, int m, int d) => LocalDay.EndUtc(new DateOnly(y, m, d), NewYork);

    [Fact]
    public void Moving_the_due_date_of_an_occurrence_does_not_move_the_next_ones()
    {
        var series = NewSeries("FREQ=MONTHLY;BYMONTHDAY=15", Eod(2026, 12, 15));
        series.RegisterInstanceClosed(Materialize(series, Eod(2026, 11, 1)));

        // La tarea del 15-dic se prorrogó al 22-dic y se cerró: la siguiente sigue siendo el 15-ene.
        var next = series.PlanNextOccurrence(Eod(2026, 12, 22), null, Eod(2026, 12, 22));

        Assert.Equal(Eod(2027, 1, 15), next.Value.DueAtUtc);
        Assert.Equal(Eod(2026, 12, 15), series.LastPlannedDueUtc);
    }

    [Fact]
    public void The_sweep_does_not_count_occurrences_that_were_created_as_skipped()
    {
        var series = NewSeries("FREQ=MONTHLY;BYMONTHDAY=15", Eod(2026, 12, 15));
        var first = Materialize(series, Eod(2026, 11, 1));
        series.RegisterInstanceClosed(first);

        // El barrido no conoce el vencimiento de la tarea cerrada.
        var next = series.PlanNextOccurrence(null, null, Eod(2027, 1, 1));

        Assert.Equal(Eod(2027, 1, 15), next.Value.DueAtUtc);
        Assert.Equal(0, next.Value.Skipped);
    }

    [Fact]
    public void A_statutory_series_closed_late_creates_the_next_obligation_even_if_it_is_already_due()
    {
        var series = NewSeries("FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=-1", Eod(2027, 1, 31), statutory: true);
        var q4 = Materialize(series, Eod(2026, 12, 1));
        series.RegisterInstanceClosed(q4);

        // El 941 de enero se cerró en agosto: el de abril no se pierde.
        var next = series.PlanNextOccurrence(Eod(2027, 1, 31), null, Eod(2027, 8, 10));

        Assert.Equal(Eod(2027, 4, 30), next.Value.DueAtUtc);
        Assert.Equal(0, next.Value.Skipped);
    }

    [Fact]
    public void A_statutory_series_created_after_its_anchor_still_creates_the_anchor()
    {
        var series = NewSeries("FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=-1", Eod(2027, 1, 31), statutory: true);

        var first = series.PlanNextOccurrence(null, null, Eod(2027, 2, 3));

        Assert.Equal(Eod(2027, 1, 31), first.Value.DueAtUtc);
    }

    [Theory]
    [InlineData(2027, 1, 31, 2027, 2, 28)]
    [InlineData(2027, 8, 31, 2027, 9, 30)]
    [InlineData(2028, 1, 30, 2028, 2, 29)]
    public void Months_after_completion_clamp_to_the_last_day_of_a_short_month(
        int y,
        int m,
        int d,
        int ey,
        int em,
        int ed
    )
    {
        var series = NewSeries("FREQ=MONTHLY", Eod(y, m, d), RecurrenceMode.AfterCompletion);
        var completed = new DateTime(y, m, d, 16, 0, 0, DateTimeKind.Utc);

        var next = series.PlanNextOccurrence(Eod(y, m, d), completed, completed);

        Assert.Equal(Eod(ey, em, ed), next.Value.DueAtUtc);
    }

    [Fact]
    public void Resume_is_only_for_a_paused_series_and_seeds_from_today()
    {
        var series = NewSeries("FREQ=MONTHLY;BYMONTHDAY=15", Eod(2026, 12, 15));
        Materialize(series, Eod(2026, 11, 1));

        Assert.Equal(TaskErrors.Series.NotPaused, series.Resume(Eod(2027, 3, 1)).Error);

        series.Pause();
        series.End();
        Assert.Equal(TaskErrors.Series.AlreadyEnded, series.Resume(Eod(2027, 3, 1)).Error);
    }

    [Fact]
    public void Resuming_does_not_bring_back_occurrences_from_the_pause()
    {
        var series = NewSeries("FREQ=MONTHLY;BYMONTHDAY=15", Eod(2026, 12, 15), statutory: true);
        series.RegisterInstanceClosed(Materialize(series, Eod(2026, 11, 1)));
        series.Pause();

        series.Resume(new DateTime(2027, 6, 2, 15, 0, 0, DateTimeKind.Utc));
        var next = series.PlanNextOccurrence(null, null, new DateTime(2027, 6, 2, 15, 0, 0, DateTimeKind.Utc));

        Assert.Equal(Eod(2027, 6, 15), next.Value.DueAtUtc);
    }

    [Fact]
    public void A_paused_series_also_follows_the_office_to_its_new_time_zone()
    {
        var series = NewSeries("FREQ=MONTHLY;BYMONTHDAY=15", Eod(2026, 12, 15));
        series.Pause();

        Assert.True(series.RezoneCivilDue("America/New_York", ResolvedTimeZone.Resolve("America/Chicago")));
    }

    /// <summary>Crea la ocurrencia que toque y devuelve su id de tarea.</summary>
    [Fact]
    public void On_a_fixed_schedule_the_first_task_is_the_first_date_of_the_rule_from_the_chosen_day()
    {
        var series = NewSeries("FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=-1", Eod(2026, 11, 23));

        Assert.Equal(Eod(2027, 1, 31), series.AnchorUtc);
    }

    [Fact]
    public void An_anchor_that_is_already_a_date_of_the_rule_and_after_completion_series_keep_their_day()
    {
        Assert.Equal(Eod(2026, 12, 15), NewSeries("FREQ=MONTHLY;BYMONTHDAY=15", Eod(2026, 12, 15)).AnchorUtc);
        Assert.Equal(
            Eod(2026, 11, 23),
            NewSeries("FREQ=DAILY;INTERVAL=90", Eod(2026, 11, 23), RecurrenceMode.AfterCompletion).AnchorUtc
        );
    }

    [Fact]
    public void Each_failed_attempt_waits_twice_as_long_up_to_a_day_and_success_resets_it()
    {
        var series = NewSeries("FREQ=MONTHLY;BYMONTHDAY=15", Eod(2026, 12, 15));
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

        series.ClaimForMaterialization(now);
        var first = series.NextMaterializationAtUtc;
        series.ClaimForMaterialization(now);
        var second = series.NextMaterializationAtUtc;
        for (var i = 0; i < 20; i++)
            series.ClaimForMaterialization(now);

        Assert.Equal(now.AddMinutes(10), first);
        Assert.Equal(now.AddMinutes(20), second);
        Assert.Equal(now.AddDays(1), series.NextMaterializationAtUtc);
        Materialize(series, Eod(2026, 11, 1));
        Assert.Equal((0, (DateTime?)null), (series.MaterializationAttempts, series.NextMaterializationAtUtc));
    }

    [Fact]
    public void In_an_annual_series_the_tax_year_moves_with_the_due_date()
    {
        var series = NewSeries("FREQ=YEARLY;BYMONTH=4;BYMONTHDAY=15", Eod(2027, 4, 15), taxYear: 2026);

        Assert.Equal(2026, series.ReferenceFor(Eod(2027, 4, 15)).TaxYear);
        Assert.Equal(2027, series.ReferenceFor(Eod(2028, 4, 15)).TaxYear);
        Assert.Equal(2029, series.ReferenceFor(Eod(2030, 4, 15)).TaxYear);
    }

    [Fact]
    public void Several_dates_a_year_keep_the_tax_year_because_a_january_date_can_belong_to_the_year_before()
    {
        var series = NewSeries("FREQ=YEARLY;BYMONTH=1,4,6,9;BYMONTHDAY=15", Eod(2027, 4, 15), taxYear: 2027);

        Assert.Equal(2027, series.ReferenceFor(Eod(2028, 1, 15)).TaxYear);
    }

    private static Guid Materialize(TaskSeries series, DateTime nowUtc)
    {
        var occurrence = series.PlanNextOccurrence(null, null, nowUtc).Value;
        var taskId = Guid.NewGuid();
        series.RegisterMaterialized(taskId, occurrence);
        return taskId;
    }

    private static TaskSeries NewSeries(
        string rule,
        DateTime anchor,
        RecurrenceMode mode = RecurrenceMode.FixedSchedule,
        bool statutory = false,
        int? taxYear = null
    ) =>
        TaskSeries
            .Create(
                TenantId,
                Ana,
                RecurrenceRule.Create(rule, "America/New_York").Value,
                mode,
                new TaskItemBlueprint
                {
                    Title = TaskTitle.Create("941 quarterly").Value,
                    Priority = TaskPriority.Normal,
                    Reference = taxYear is { } year ? TaskReference.Create(null, year).Value : TaskReference.None,
                    AssigneeUserId = Ana,
                    IsStatutory = statutory,
                },
                anchor,
                null,
                null,
                anchor.AddDays(-60)
            )
            .Value;
}
