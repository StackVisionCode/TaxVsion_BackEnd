using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Consumers;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;
using Xunit;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// Al retirar (offboard) a un empleado: sus tareas abiertas asignadas se reasignan al sucesor (o se
/// desasignan) y sus series redirigen las ocurrencias futuras (o se pausan). Lo de otros no se toca.
/// </summary>
public sealed class TasksUserOffboardedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static TaskItem OpenTask(Guid assignee) =>
        TaskItem
            .Create(
                Tenant,
                Guid.NewGuid(),
                TaskTitle.Create("Open task").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                assignee,
                DateTime.UtcNow
            )
            .Value;

    private static TaskSeries SeriesFor(Guid assignee) =>
        TaskSeries
            .Create(
                Tenant,
                Guid.NewGuid(),
                RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO", "America/New_York").Value,
                RecurrenceMode.FixedSchedule,
                new TaskItemBlueprint
                {
                    Title = TaskTitle.Create("Quarterly filing").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.None,
                    AssigneeUserId = assignee,
                    IsStatutory = false,
                },
                DateTime.UtcNow.AddDays(1),
                null,
                null,
                DateTime.UtcNow
            )
            .Value;

    private static Task Run(
        InMemoryTaskRepository tasks,
        InMemorySeries series,
        RecordingUnitOfWork uow,
        FakeMessageBus bus,
        Guid leaver,
        Guid? successor
    ) =>
        TasksUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                SuccessorUserId = successor,
                RemovedAtUtc = DateTime.UtcNow,
            },
            tasks,
            series,
            uow,
            bus,
            new NoOpCorrelationContext(),
            NullLogger<TaskItem>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task With_successor_reassigns_open_tasks_and_redirects_series()
    {
        var leaver = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var mine = OpenTask(leaver);
        var other = OpenTask(Guid.NewGuid());
        var tasks = new InMemoryTaskRepository(mine, other);
        var mySeries = SeriesFor(leaver);
        var otherSeries = SeriesFor(Guid.NewGuid());
        var series = new InMemorySeries(mySeries, otherSeries);
        var uow = new RecordingUnitOfWork();
        var bus = new FakeMessageBus();

        await Run(tasks, series, uow, bus, leaver, successor);

        Assert.Equal(successor, mine.AssigneeUserId);
        Assert.NotEqual(successor, other.AssigneeUserId);
        Assert.Equal(successor, mySeries.Blueprint.AssigneeUserId);
        Assert.NotEqual(successor, otherSeries.Blueprint.AssigneeUserId);
        Assert.Single(bus.Published.OfType<TaskAssignedIntegrationEvent>());
    }

    [Fact]
    public async Task Without_successor_unassigns_open_tasks_and_pauses_series()
    {
        var leaver = Guid.NewGuid();
        var mine = OpenTask(leaver);
        var tasks = new InMemoryTaskRepository(mine);
        var mySeries = SeriesFor(leaver);
        var series = new InMemorySeries(mySeries);
        var uow = new RecordingUnitOfWork();
        var bus = new FakeMessageBus();

        await Run(tasks, series, uow, bus, leaver, successor: null);

        Assert.Null(mine.AssigneeUserId);
        Assert.Equal(SeriesStatus.Paused, mySeries.Status);
        Assert.Empty(bus.Published.OfType<TaskAssignedIntegrationEvent>());
    }

    private sealed class InMemorySeries(params TaskSeries[] seed) : ITaskSeriesRepository
    {
        private readonly List<TaskSeries> _all = [.. seed];

        public void Add(TaskSeries series) => _all.Add(series);

        public Task<Result<TaskSeries>> GetByIdAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default) =>
            Task.FromResult(
                _all.FirstOrDefault(s => s.TenantId == tenantId && s.Id == seriesId) is { } found
                    ? Result.Success(found)
                    : Result.Failure<TaskSeries>(TaskErrors.Series.NotFound)
            );

        public Task<IReadOnlyList<TaskSeries>> ListAsync(
            Guid tenantId,
            SeriesStatus? status,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<TaskSeries>>(
                _all.Where(s => s.TenantId == tenantId && (status is not { } w || s.Status == w)).ToList()
            );

        public Task<IReadOnlyList<TaskSeries>> ListPendingMaterializationAsync(
            int take,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TaskSeries>>([]);
    }
}
