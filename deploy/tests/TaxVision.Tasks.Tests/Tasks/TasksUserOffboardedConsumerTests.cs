using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Series;
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

    private static TaskItem OpenTask(Guid assignee, DueDate? due = null) =>
        TaskItem
            .Create(
                Tenant,
                Guid.NewGuid(),
                TaskTitle.Create("Open task").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                due,
                null,
                assignee,
                DateTime.UtcNow
            )
            .Value;

    private static TaskSeries SeriesFor(Guid assignee, Guid? createdBy = null) =>
        TaskSeries
            .Create(
                Tenant,
                createdBy ?? Guid.NewGuid(),
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

    // El aviso y el recordatorio del sucesor salen del handler del evento de dominio (el replay los deja en el bus).
    private static async Task Run(
        InMemoryTaskRepository tasks,
        InMemorySeries series,
        RecordingUnitOfWork uow,
        FakeMessageBus bus,
        Guid leaver,
        Guid? successor,
        Guid? offboardedBy = null
    )
    {
        await TasksUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                SuccessorUserId = successor,
                OffboardedByUserId = offboardedBy,
                RemovedAtUtc = DateTime.UtcNow,
            },
            tasks,
            series,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<TaskItem>.Instance,
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, tasks);
    }

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

    [Fact]
    public async Task The_successor_requests_the_due_reminder_of_each_reassigned_task()
    {
        var leaver = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var due = DueDate.Create(DateTime.UtcNow.AddDays(3), "America/New_York", false).Value;
        var withDue = OpenTask(leaver, due);
        var withoutDue = OpenTask(leaver);
        var bus = new FakeMessageBus();

        await Run(
            new InMemoryTaskRepository(withDue, withoutDue),
            new InMemorySeries(),
            new RecordingUnitOfWork(),
            bus,
            leaver,
            successor
        );

        var reminder = Assert.Single(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Equal(successor, reminder.UserId);
        Assert.Equal(withDue.Id, reminder.TargetId);
        Assert.Contains(successor.ToString("N"), reminder.RequestKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_successor_no_reminder_is_requested()
    {
        var leaver = Guid.NewGuid();
        var due = DueDate.Create(DateTime.UtcNow.AddDays(3), "America/New_York", false).Value;
        var bus = new FakeMessageBus();

        await Run(
            new InMemoryTaskRepository(OpenTask(leaver, due)),
            new InMemorySeries(),
            new RecordingUnitOfWork(),
            bus,
            leaver,
            successor: null
        );

        Assert.Empty(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task With_successor_the_series_the_leaver_managed_pass_to_the_successor()
    {
        var leaver = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var assignee = Guid.NewGuid();
        var managed = SeriesFor(assignee, createdBy: leaver);

        await Run(
            new InMemoryTaskRepository(),
            new InMemorySeries(managed),
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            leaver,
            successor
        );

        Assert.Equal(leaver, managed.CreatedByUserId);
        Assert.Equal(successor, managed.EffectiveOwnerUserId);
        Assert.Equal(assignee, managed.Blueprint.AssigneeUserId);
        Assert.Equal(SeriesStatus.Active, managed.Status);
        Assert.True(TaskSeriesAccessPolicy.CanMutate(managed, successor, hasManageAll: false));
        Assert.False(TaskSeriesAccessPolicy.CanMutate(managed, leaver, hasManageAll: false));
    }

    [Fact]
    public async Task Without_successor_the_series_the_leaver_managed_pass_to_whoever_offboarded()
    {
        var leaver = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var managedAndAssigned = SeriesFor(leaver, createdBy: leaver);

        await Run(
            new InMemoryTaskRepository(),
            new InMemorySeries(managedAndAssigned),
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            leaver,
            successor: null,
            offboardedBy: admin
        );

        Assert.Equal(admin, managedAndAssigned.EffectiveOwnerUserId);
        Assert.Equal(SeriesStatus.Paused, managedAndAssigned.Status);
    }

    [Fact]
    public async Task New_occurrences_are_created_by_the_effective_owner()
    {
        var creator = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var series = SeriesFor(Guid.NewGuid(), createdBy: creator);

        Assert.True(series.TransferOwnership(successor).IsSuccess);
        var materializer = new TaskSeriesMaterializer(new InMemoryTaskRepository(), new InMemorySeries(series));
        var created = await materializer.MaterializeNextAsync(series, null, null);

        Assert.True(created.IsSuccess);
        Assert.Equal(successor, created.Value.CreatedByUserId);
        Assert.True(series.TransferOwnership(creator).IsSuccess);
        Assert.Null(series.OwnerUserId);
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
