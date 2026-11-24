using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Permissions.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Permissions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

// Reabrir una tarea cuyo asignado ya no está activo (p. ej. retirado) la deja sin asignar.
public sealed class ReopenTaskHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Reopening_a_task_of_an_inactive_assignee_leaves_it_unassigned()
    {
        var leaver = Guid.NewGuid();
        var task = ClosedTask(leaver);
        var projection = UserPermissionsProjection.Create(TenantId, leaver, 1, [], []);
        projection.MarkInactive();

        var result = await ReopenAsync(task, new Users(projection));

        Assert.True(result.IsSuccess);
        Assert.Null(task.AssigneeUserId);
        Assert.Equal(TaskItemStatus.NotStarted, task.Status);
    }

    /// <summary>S5: la serie ya creó la siguiente; reabrir esta dejaría dos abiertas.</summary>
    [Fact]
    public async Task An_occurrence_already_replaced_by_the_next_one_cannot_be_reopened()
    {
        var series = TaxVision
            .Tasks.Domain.Series.TaskSeries.Create(
                TenantId,
                Manager,
                RecurrenceRule.Create("FREQ=MONTHLY;BYMONTHDAY=15", "America/New_York").Value,
                TaxVision.Tasks.Domain.Series.RecurrenceMode.FixedSchedule,
                new TaxVision.Tasks.Domain.Series.TaskItemBlueprint
                {
                    Title = TaskTitle.Create("Payroll").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.None,
                    AssigneeUserId = Manager,
                    IsStatutory = false,
                },
                Now.AddDays(10),
                null,
                null,
                Now
            )
            .Value;
        var first = ClosedTask(Manager);
        first.AttachToSeries(series.Id, 1);
        series.RegisterMaterialized(first.Id, series.PlanNextOccurrence(null, null, Now).Value);
        series.RegisterInstanceClosed(first.Id);
        series.RegisterMaterialized(Guid.NewGuid(), series.PlanNextOccurrence(null, null, Now).Value);

        var result = await ReopenAsync(first, new Users(), series: new InMemoryTaskSeriesRepository(series));

        Assert.Equal(TaskErrors.Series.OccurrenceSuperseded, result.Error);
        Assert.Equal(TaskItemStatus.Completed, first.Status);
    }

    [Fact]
    public async Task An_active_assignee_keeps_the_reopened_task()
    {
        var assignee = Guid.NewGuid();
        var task = ClosedTask(assignee);

        await ReopenAsync(task, new Users(UserPermissionsProjection.Create(TenantId, assignee, 1, [], [])));

        Assert.Equal(assignee, task.AssigneeUserId);
    }

    [Fact]
    public async Task Without_a_projection_the_assignee_is_kept()
    {
        var assignee = Guid.NewGuid();
        var task = ClosedTask(assignee);

        await ReopenAsync(task, new Users());

        Assert.Equal(assignee, task.AssigneeUserId);
    }

    [Fact]
    public async Task Reopen_WithDueAndAssignee_RequestsReminderWithLifecycleKey()
    {
        var assignee = Guid.NewGuid();
        var task = ClosedTask(assignee, DueDate.Create(DateTime.UtcNow.AddDays(3), "America/New_York", false).Value);
        var bus = new FakeMessageBus();

        await ReopenAsync(task, new Users(UserPermissionsProjection.Create(TenantId, assignee, 1, [], [])), bus);

        var reminder = Assert.Single(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        Assert.EndsWith(":r1", reminder.RequestKey);
        Assert.Equal(assignee, reminder.UserId);
        Assert.Equal(task.Due!.DueAtUtc, reminder.AnchorAtUtc);
    }

    [Fact]
    public async Task ReopenTwice_UsesDistinctKeys()
    {
        var assignee = Guid.NewGuid();
        var task = ClosedTask(assignee, DueDate.Create(DateTime.UtcNow.AddDays(3), "America/New_York", false).Value);
        var users = new Users(UserPermissionsProjection.Create(TenantId, assignee, 1, [], []));
        var bus = new FakeMessageBus();

        await ReopenAsync(task, users, bus);
        task.Complete(Manager, DateTime.UtcNow);
        await ReopenAsync(task, users, bus);

        var keys = bus.Published.OfType<ReminderRequestedIntegrationEvent>().Select(r => r.RequestKey).ToList();
        Assert.Equal(2, keys.Count);
        Assert.EndsWith(":r1", keys[0]);
        Assert.EndsWith(":r2", keys[1]);
    }

    [Fact]
    public async Task Reopening_a_subtask_of_a_closed_task_asks_first_and_changes_nothing()
    {
        var (parent, child) = CompletedParentWithSubtask();
        var tree = new Tree(parent, child);

        var result = await tree.ReopenAsync(child, reopenParent: false);

        Assert.Equal("Task.Subtask.ParentClosed", result.Error.Code);
        Assert.Equal(TaskItemStatus.Completed, child.Status);
        Assert.Equal(TaskItemStatus.Completed, parent.Status);
    }

    [Fact]
    public async Task With_consent_the_closed_task_reopens_with_its_subtask()
    {
        var (parent, child) = CompletedParentWithSubtask();
        var tree = new Tree(parent, child);

        var result = await tree.ReopenAsync(child, reopenParent: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(TaskItemStatus.NotStarted, child.Status);
        Assert.Equal(TaskItemStatus.NotStarted, parent.Status);
        Assert.Equal(1, parent.OpenSubtaskCount);
    }

    [Fact]
    public async Task A_subtask_of_an_open_task_reopens_without_asking()
    {
        var parent = OpenTask();
        var child = NewSubtask(parent);
        child.Complete(Manager, Now);
        parent.RegisterSubtaskClosed();
        var tree = new Tree(parent, child);

        var result = await tree.ReopenAsync(child, reopenParent: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, parent.OpenSubtaskCount);
    }

    [Fact]
    public async Task Reopening_someone_elses_closed_task_needs_permission_on_it()
    {
        var preparer = Guid.NewGuid();
        var parent = OpenTask();
        var child = TaskItem
            .CreateSubtask(
                parent,
                Manager,
                TaskTitle.Create("Collect W-2").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                preparer,
                Now
            )
            .Value;
        child.Complete(preparer, Now);
        parent.RegisterSubtaskClosed();
        parent.Complete(Manager, Now);
        var tree = new Tree(parent, child);

        var result = await tree.ReopenAsync(child, reopenParent: true, byUserId: preparer, hasManageAll: false);

        Assert.Equal(TaskErrors.Forbidden, result.Error);
        Assert.Equal(TaskItemStatus.Completed, child.Status);
    }

    /// <summary>Servicios reales de jerarquía y desbloqueo: los contadores cambian de verdad.</summary>
    private sealed class Tree(params TaskItem[] seed)
    {
        private readonly InMemoryTaskRepository _tasks = new(seed);
        private readonly InMemoryTaskDependencyRepository _dependencies = new();

        public Task<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>> ReopenAsync(
            TaskItem task,
            bool reopenParent,
            Guid? byUserId = null,
            bool hasManageAll = true
        ) =>
            ReopenTaskHandler.Handle(
                new ReopenTaskCommand(TenantId, task.Id, byUserId ?? Manager, hasManageAll, reopenParent),
                _tasks,
                new InMemoryTaskSeriesRepository(),
                new TaxVision.Tasks.Application.Dependencies.TaskUnblockingService(
                    _tasks,
                    _dependencies,
                    new NoOpCorrelationContext()
                ),
                new TaxVision.Tasks.Application.Hierarchy.TaskHierarchyService(_tasks, _dependencies),
                new Users(),
                new RecordingUnitOfWork(),
                CancellationToken.None
            );
    }

    private static (TaskItem Parent, TaskItem Child) CompletedParentWithSubtask()
    {
        var parent = OpenTask();
        var child = NewSubtask(parent);
        child.Complete(Manager, Now);
        parent.RegisterSubtaskClosed();
        parent.Complete(Manager, Now);
        return (parent, child);
    }

    private static TaskItem OpenTask() =>
        TaskItem
            .Create(
                TenantId,
                Manager,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Manager,
                Now
            )
            .Value;

    private static TaskItem NewSubtask(TaskItem parent) =>
        TaskItem
            .CreateSubtask(
                parent,
                Manager,
                TaskTitle.Create("Collect W-2").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                Manager,
                Now
            )
            .Value;

    // TaskReopened y el recordatorio salen del handler del evento de dominio (el replay los deja en el bus).
    private static async Task<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>> ReopenAsync(
        TaskItem task,
        IUserPermissionsProjectionRepository users,
        FakeMessageBus? bus = null,
        InMemoryTaskSeriesRepository? series = null
    )
    {
        var tasks = new InMemoryTaskRepository(task);
        var result = await ReopenTaskHandler.Handle(
            new ReopenTaskCommand(TenantId, task.Id, Manager, HasManageAll: true),
            tasks,
            series ?? new InMemoryTaskSeriesRepository(),
            new NoOpUnblocking(),
            new NoOpHierarchy(),
            users,
            new RecordingUnitOfWork(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus ?? new FakeMessageBus(), tasks);
        return result;
    }

    private static TaskItem ClosedTask(Guid assignee, DueDate? due = null)
    {
        var task = TaskItem
            .Create(
                TenantId,
                Manager,
                TaskTitle.Create("Prepare return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                due,
                null,
                assignee,
                Now
            )
            .Value;
        task.Complete(Manager, Now.AddHours(1));
        return task;
    }

    private sealed class Users(params UserPermissionsProjection[] seed) : IUserPermissionsProjectionRepository
    {
        public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(seed.FirstOrDefault(p => p.TenantId == tenantId && p.UserId == userId));

        public Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserPermissionsProjection>>([]);
    }

    private sealed class NoOpUnblocking : ITaskUnblockingService
    {
        public Task<IReadOnlyList<TaskUnblockedIntegrationEvent>> ApplyPredecessorClosedAsync(
            Guid tenantId,
            Guid predecessorTaskId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TaskUnblockedIntegrationEvent>>([]);

        public Task ApplyPredecessorReopenedAsync(
            Guid tenantId,
            Guid predecessorTaskId,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class NoOpHierarchy : ITaskHierarchyService
    {
        public Task ApplyChildClosedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ApplyChildReopenedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Guid>> DeleteWithDescendantsAsync(
            Guid tenantId,
            Guid taskId,
            Guid byUserId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
