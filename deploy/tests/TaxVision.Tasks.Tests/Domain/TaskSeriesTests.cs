using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class TaskSeriesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();

    /// <summary>15-abr, 15-jun, 15-sep, 15-ene: el 1040-ES trimestral.</summary>
    private const string Quarterly = "FREQ=MONTHLY;INTERVAL=3";

    private const string Every90Days = "FREQ=DAILY;INTERVAL=90";

    private static readonly DateTime Anchor = new(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_first_occurrence_is_the_anchor_itself()
    {
        var series = NewSeries(Quarterly, RecurrenceMode.FixedSchedule);

        var first = series.PlanNextOccurrence(null, null, Anchor.AddDays(-14));

        Assert.Equal(Anchor, first.Value.DueAtUtc);
        Assert.Equal(1, first.Value.Number);
    }

    /// <summary>Checkpoint 8.1: cerrar Q1 tarde no corre el vencimiento de Q2.</summary>
    [Fact]
    public void A_fixed_schedule_series_ignores_how_late_the_instance_was_closed()
    {
        var series = NewSeries(Quarterly, RecurrenceMode.FixedSchedule);
        var lateClose = new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc);

        var next = series.PlanNextOccurrence(Anchor, lateClose, lateClose);

        Assert.Equal(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc), next.Value.DueAtUtc);
    }

    /// <summary>Checkpoint 8.2: «cada 90 días» cuenta desde el repaso real, no desde el calendario.</summary>
    [Fact]
    public void An_after_completion_series_counts_from_the_day_it_was_closed()
    {
        var series = NewSeries(Every90Days, RecurrenceMode.AfterCompletion);
        var lateClose = new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc);

        var next = series.PlanNextOccurrence(Anchor, lateClose, lateClose);

        Assert.Equal(lateClose.AddDays(90), next.Value.DueAtUtc);
    }

    /// <summary>
    /// Checkpoint 8.3: con una instancia abierta no se materializa otra, ni aunque alguien reintente.
    /// </summary>
    [Fact]
    public void A_series_with_an_open_instance_plans_nothing()
    {
        var series = NewSeries(Quarterly, RecurrenceMode.FixedSchedule);
        var first = series.PlanNextOccurrence(null, null, Anchor.AddDays(-14)).Value;
        series.RegisterMaterialized(Guid.NewGuid(), first);

        var second = series.PlanNextOccurrence(Anchor, null, Anchor.AddDays(1));

        Assert.Equal(TaskErrors.Series.InstanceStillOpen, second.Error);
    }

    /// <summary>Cerrar una ocurrencia vieja no debe liberar el hueco de la que está abierta.</summary>
    [Fact]
    public void Only_the_open_instance_releases_the_series()
    {
        var series = NewSeries(Quarterly, RecurrenceMode.FixedSchedule);
        var first = series.PlanNextOccurrence(null, null, Anchor.AddDays(-14)).Value;
        var openId = Guid.NewGuid();
        series.RegisterMaterialized(openId, first);

        Assert.False(series.RegisterInstanceClosed(Guid.NewGuid()));
        Assert.Equal(openId, series.OpenInstanceId);
        Assert.True(series.RegisterInstanceClosed(openId));
        Assert.Null(series.OpenInstanceId);
    }

    /// <summary>
    /// Ocho meses sin tocar la serie: se materializa una sola futura y las de atrás quedan contadas,
    /// no creadas.
    /// </summary>
    [Fact]
    public void A_long_backlog_skips_the_past_occurrences_and_counts_them()
    {
        var series = NewSeries(Quarterly, RecurrenceMode.FixedSchedule);
        var muchLater = Anchor.AddMonths(8);

        var next = series.PlanNextOccurrence(Anchor, null, muchLater);

        Assert.True(next.Value.DueAtUtc > muchLater);
        Assert.Equal(2, next.Value.Skipped);
    }

    [Fact]
    public void A_paused_series_plans_nothing_and_resuming_reseeds_from_now()
    {
        var series = NewSeries(Quarterly, RecurrenceMode.FixedSchedule);
        series.Pause();

        Assert.Equal(TaskErrors.Series.NotActive, series.PlanNextOccurrence(null, null, Anchor).Error);

        var resumedAt = Anchor.AddMonths(14);
        series.Resume(resumedAt);

        Assert.Equal(resumedAt, series.AnchorUtc);
        Assert.Equal(SeriesStatus.Active, series.Status);
    }

    [Fact]
    public void Reaching_the_occurrence_limit_ends_the_series()
    {
        var series = NewSeries(Quarterly, RecurrenceMode.FixedSchedule, maxOccurrences: 1);
        var first = series.PlanNextOccurrence(null, null, Anchor.AddDays(-14)).Value;

        series.RegisterMaterialized(Guid.NewGuid(), first);

        Assert.Equal(SeriesStatus.Ended, series.Status);
    }

    /// <summary>Un UNTIL vencido no deja materializar más: la regla se agotó, no falló.</summary>
    [Fact]
    public void A_series_past_its_end_date_yields_no_occurrence()
    {
        var series = NewSeries(Quarterly, RecurrenceMode.FixedSchedule, endsAtUtc: Anchor.AddMonths(2));

        var next = series.PlanNextOccurrence(Anchor, null, Anchor.AddDays(1));

        Assert.Equal(TaskErrors.Series.NoFurtherOccurrence, next.Error);
    }

    private static TaskSeries NewSeries(
        string rule,
        RecurrenceMode mode,
        DateTime? endsAtUtc = null,
        int? maxOccurrences = null
    ) =>
        TaskSeries
            .Create(
                TenantId,
                Ana,
                RecurrenceRule.Create(rule, "America/New_York").Value,
                mode,
                new TaskItemBlueprint
                {
                    Title = TaskTitle.Create("1040-ES trimestral").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.None,
                    AssigneeUserId = Ana,
                    IsStatutory = true,
                },
                Anchor,
                endsAtUtc,
                maxOccurrences,
                Anchor.AddDays(-30)
            )
            .Value;
}
