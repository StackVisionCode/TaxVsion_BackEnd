using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Queries;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Series;

/// <summary>El historial de una serie: sus tareas, la más reciente primero, con la visibilidad de la serie.</summary>
public sealed class ListSeriesOccurrencesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public async Task Lists_the_tasks_of_the_series_newest_first()
    {
        var (series, tasks) = await SeriesWithOccurrencesAsync(null);

        var result = await ListAsync(series, tasks, Owner);

        Assert.True(result.IsSuccess);
        Assert.Equal([2, 1], result.Value.Select(o => o.OccurrenceNumber));
    }

    [Fact]
    public async Task Someone_who_cannot_see_the_series_gets_not_found()
    {
        var (series, tasks) = await SeriesWithOccurrencesAsync(Guid.NewGuid());

        var result = await ListAsync(series, tasks, Guid.NewGuid());

        Assert.Equal(TaskErrors.Series.NotFound, result.Error);
    }

    private static Task<BuildingBlocks.Results.Result<IReadOnlyList<SeriesOccurrenceResponse>>> ListAsync(
        InMemoryTaskSeriesRepository series,
        InMemoryTaskRepository tasks,
        Guid viewer
    ) =>
        ListSeriesOccurrencesHandler.Handle(
            new ListSeriesOccurrencesQuery(TenantId, series.Stored[0].Id, viewer, CanViewAll: false),
            series,
            tasks,
            new OnlyTheseCustomers(),
            Options.Create(new TasksVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );

    private static async Task<(InMemoryTaskSeriesRepository, InMemoryTaskRepository)> SeriesWithOccurrencesAsync(
        Guid? customer
    )
    {
        var series = TaskSeries
            .Create(
                TenantId,
                Owner,
                RecurrenceRule.Create("FREQ=MONTHLY;BYMONTHDAY=15", "America/New_York").Value,
                RecurrenceMode.FixedSchedule,
                new TaskItemBlueprint
                {
                    Title = TaskTitle.Create("Monthly bookkeeping").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.Create(customer, null).Value,
                    AssigneeUserId = Owner,
                    IsStatutory = false,
                },
                DateTime.UtcNow.AddDays(10),
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
        var tasks = new InMemoryTaskRepository();
        foreach (var number in new[] { 1, 2 })
        {
            var task = TaskItem
                .Create(
                    TenantId,
                    Owner,
                    TaskTitle.Create("Monthly bookkeeping").Value,
                    null,
                    TaskPriority.Normal,
                    TaskReference.Create(customer, null).Value,
                    null,
                    null,
                    Owner,
                    DateTime.UtcNow
                )
                .Value;
            task.AttachToSeries(series.Id, number);
            tasks.Add(task);
        }
        await Task.CompletedTask;
        return (new InMemoryTaskSeriesRepository(series), tasks);
    }
}
