using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Commands;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Templates;

namespace TaxVision.Tasks.Tests.Series;

/// <summary>
/// Editar una serie cambia las ocurrencias futuras; la abierta solo cambia si se pide. Un calendario nuevo empieza en
/// su primera fecha desde "Starts on", la misma que muestra la vista previa.
/// </summary>
public sealed class UpdateTaskSeriesHandlerTests
{
    private const string Zone = "America/New_York";
    private const string Monthly15 = "FREQ=MONTHLY;BYMONTHDAY=15";
    private const string Quarterly941 = "FREQ=YEARLY;BYMONTH=1,4,7,10;BYMONTHDAY=-1";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();
    private static readonly ResolvedTimeZone Office = ResolvedTimeZone.Resolve(Zone);

    private static DateTime EndOf(int y, int m, int d) => LocalDay.EndUtc(new DateOnly(y, m, d), Office);

    [Fact]
    public async Task The_details_change_the_next_tasks_and_the_open_one_only_when_asked()
    {
        var (series, tasks, repo) = await SeriesWithOpenTaskAsync();
        var open = tasks.Stored.Single();

        var kept = await UpdateAsync(
            repo,
            tasks,
            Command(series) with
            {
                Title = "Payroll (941)",
                Priority = TaskPriority.Urgent,
                AssigneeUserId = Reviewer,
            }
        );

        Assert.True(kept.IsSuccess);
        Assert.Equal("Payroll (941)", series.Blueprint.Title.Value);
        Assert.Equal(Reviewer, series.Blueprint.AssigneeUserId);
        Assert.Equal("Monthly bookkeeping", open.Title.Value);
        Assert.Equal(Preparer, open.AssigneeUserId);

        var applied = await UpdateAsync(
            repo,
            tasks,
            Command(series) with
            {
                Title = "Payroll (941)",
                Priority = TaskPriority.Urgent,
                AssigneeUserId = Reviewer,
                ApplyToOpenInstance = true,
            }
        );

        Assert.True(applied.IsSuccess);
        Assert.Equal("Payroll (941)", open.Title.Value);
        Assert.Equal(TaskPriority.Urgent, open.Priority);
        Assert.Equal(Reviewer, open.AssigneeUserId);
    }

    [Fact]
    public async Task A_new_schedule_starts_on_its_first_date_after_the_open_task_and_keeps_that_task()
    {
        var (series, tasks, repo) = await SeriesWithOpenTaskAsync();
        var open = tasks.Stored.Single();

        var result = await UpdateAsync(
            repo,
            tasks,
            Command(series) with
            {
                Rule = Quarterly941,
                StartsOn = new DateOnly(2027, 1, 16),
            }
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(EndOf(2027, 1, 15), open.Due!.DueAtUtc);
        Assert.Equal(new DateOnly(2027, 1, 15), result.Value.NextDueOn);
        series.RegisterInstanceClosed(open.Id);
        var next = series.PlanNextOccurrence(
            open.Due!.DueAtUtc,
            null,
            new DateTime(2027, 1, 16, 0, 0, 0, DateTimeKind.Utc)
        );
        Assert.Equal(EndOf(2027, 1, 31), next.Value.DueAtUtc);
        series.RegisterMaterialized(Guid.NewGuid(), next.Value);
        series.RegisterInstanceClosed(series.OpenInstanceId!.Value);
        var after = series.PlanNextOccurrence(
            next.Value.DueAtUtc,
            null,
            new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        Assert.Equal(EndOf(2027, 4, 30), after.Value.DueAtUtc);
    }

    [Fact]
    public async Task Without_a_start_date_the_new_schedule_starts_the_day_after_the_open_task()
    {
        var (series, tasks, repo) = await SeriesWithOpenTaskAsync();

        var result = await UpdateAsync(repo, tasks, Command(series) with { Rule = "FREQ=MONTHLY;BYMONTHDAY=20" });

        Assert.True(result.IsSuccess);
        series.RegisterInstanceClosed(tasks.Stored.Single().Id);
        var next = series.PlanNextOccurrence(null, null, new DateTime(2027, 1, 16, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(EndOf(2027, 1, 20), next.Value.DueAtUtc);
    }

    [Fact]
    public async Task A_new_schedule_cannot_start_before_the_open_task_is_due()
    {
        var (series, tasks, repo) = await SeriesWithOpenTaskAsync();

        var result = await UpdateAsync(
            repo,
            tasks,
            Command(series) with
            {
                Rule = "FREQ=MONTHLY;BYMONTHDAY=10",
                StartsOn = new DateOnly(2027, 1, 1),
            }
        );

        Assert.Equal(TaskErrors.Series.StartsBeforeCurrent, result.Error);
        Assert.Equal(Monthly15, series.Rule.Value);
    }

    [Fact]
    public async Task The_limit_cannot_go_below_the_tasks_already_created()
    {
        var (series, tasks, repo) = await SeriesWithOpenTaskAsync();

        var below = await UpdateAsync(repo, tasks, Command(series) with { MaxOccurrences = 0 });
        var ends = await UpdateAsync(repo, tasks, Command(series) with { EndsOn = new DateOnly(2027, 1, 10) });

        Assert.Equal(TaskErrors.Series.MaxOccurrencesInvalid, below.Error);
        Assert.Equal(TaskErrors.Series.EndsBeforeNext, ends.Error);
    }

    [Fact]
    public async Task The_next_tasks_go_to_the_new_client()
    {
        var (series, tasks, repo) = await SeriesWithOpenTaskAsync();
        var acme = Guid.NewGuid();

        var result = await UpdateAsync(repo, tasks, Command(series) with { CustomerId = acme });

        Assert.True(result.IsSuccess);
        Assert.Equal(acme, series.Blueprint.Reference.CustomerId);
    }

    [Fact]
    public async Task An_ended_series_and_someone_else_series_cannot_be_edited()
    {
        var (series, tasks, repo) = await SeriesWithOpenTaskAsync();

        var stranger = await UpdateAsync(repo, tasks, Command(series) with { ByUserId = Guid.NewGuid() });
        series.End();
        var ended = await UpdateAsync(repo, tasks, Command(series));

        Assert.Equal(TaskErrors.Forbidden, stranger.Error);
        Assert.Equal(TaskErrors.Series.AlreadyEnded, ended.Error);
    }

    private static UpdateTaskSeriesCommand Command(TaskSeries series) =>
        new(
            TenantId,
            series.Id,
            Owner,
            false,
            series.Blueprint.Title.Value,
            series.Blueprint.Description?.Value,
            series.Blueprint.Priority,
            series.Blueprint.AssigneeUserId,
            series.Blueprint.IsStatutory,
            series.Rule.Value,
            series.Mode,
            null,
            null,
            null,
            false,
            series.Blueprint.Reference.CustomerId,
            null
        );

    private static Task<BuildingBlocks.Results.Result<TaskSeriesResponse>> UpdateAsync(
        InMemoryTaskSeriesRepository repo,
        InMemoryTaskRepository tasks,
        UpdateTaskSeriesCommand command
    ) =>
        UpdateTaskSeriesHandler.Handle(
            command,
            repo,
            tasks,
            new RecordingUnitOfWork(),
            new FixedOfficeZone(Zone),
            new EveryCustomer(),
            CancellationToken.None
        );

    /// <summary>Serie mensual del 15 con su primera tarea abierta, que vence el 15-ene-2027.</summary>
    private static async Task<(
        TaskSeries,
        InMemoryTaskRepository,
        InMemoryTaskSeriesRepository
    )> SeriesWithOpenTaskAsync()
    {
        var series = TaskSeries
            .Create(
                TenantId,
                Owner,
                RecurrenceRule.Create(Monthly15, Zone).Value,
                RecurrenceMode.FixedSchedule,
                new TaskItemBlueprint
                {
                    Title = TaskTitle.Create("Monthly bookkeeping").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.None,
                    AssigneeUserId = Preparer,
                    IsStatutory = false,
                },
                EndOf(2027, 1, 15),
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
        var tasks = new InMemoryTaskRepository();
        var repo = new InMemoryTaskSeriesRepository(series);
        var first = await new TaskSeriesMaterializer(
            tasks,
            repo,
            new InMemoryTaskTemplateRepository()
        ).MaterializeFirstAsync(series, CancellationToken.None);
        Assert.True(first.IsSuccess);
        return (series, tasks, repo);
    }
}
