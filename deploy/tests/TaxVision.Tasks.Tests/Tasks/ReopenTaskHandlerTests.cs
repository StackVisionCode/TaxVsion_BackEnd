using BuildingBlocks.Messaging.ReminderIntegrationEvents;
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

    private static Task<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>> ReopenAsync(
        TaskItem task,
        IUserPermissionsProjectionRepository users,
        FakeMessageBus? bus = null
    ) =>
        ReopenTaskHandler.Handle(
            new ReopenTaskCommand(TenantId, task.Id, Manager, HasManageAll: true),
            new InMemoryTaskRepository(task),
            new NoOpUnblocking(),
            new NoOpHierarchy(),
            users,
            new RecordingUnitOfWork(),
            bus ?? new FakeMessageBus(),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

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
        public Task ApplyPredecessorClosedAsync(
            Guid tenantId,
            Guid predecessorTaskId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

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

        public Task DeleteWithDescendantsAsync(
            Guid tenantId,
            Guid taskId,
            Guid byUserId,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }
}
