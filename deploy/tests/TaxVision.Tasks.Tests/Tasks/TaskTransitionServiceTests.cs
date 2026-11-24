using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Tasks;

public sealed class TaskTransitionServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Load_returns_the_task_for_someone_who_can_change_it()
    {
        var task = NewTask();
        var tasks = new InMemoryTaskRepository(task);

        var loaded = await TaskTransitionService.LoadMutableAsync(tasks, Command(task.Id, Owner, false), default);

        Assert.True(loaded.IsSuccess);
        Assert.Same(task, loaded.Value);
    }

    [Fact]
    public async Task Load_rejects_a_stranger_without_the_supervision_override()
    {
        var task = NewTask();
        var tasks = new InMemoryTaskRepository(task);

        var denied = await TaskTransitionService.LoadMutableAsync(tasks, Command(task.Id, Stranger, false), default);
        var allowed = await TaskTransitionService.LoadMutableAsync(tasks, Command(task.Id, Stranger, true), default);

        Assert.Equal(TaskErrors.Forbidden, denied.Error);
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public async Task Load_of_a_missing_task_is_not_found()
    {
        var tasks = new InMemoryTaskRepository();

        var loaded = await TaskTransitionService.LoadMutableAsync(
            tasks,
            Command(Guid.NewGuid(), Owner, false),
            default
        );

        Assert.Equal(TaskErrors.NotFound, loaded.Error);
    }

    [Fact]
    public async Task Closing_frees_successors_updates_the_parent_and_advances_the_series()
    {
        var parent = NewTask();
        var parentId = parent.Id;
        var task = NewSubtask(parent);
        var unblocking = new RecordingUnblocking();
        var hierarchy = new RecordingHierarchy();
        var series = new RecordingSeriesMaterializer();

        var notices = await TaskTransitionService.ApplyClosedAsync(task, unblocking, hierarchy, series, default);

        Assert.Equal([(TenantId, task.Id)], unblocking.Closed);
        Assert.Equal([(TenantId, (Guid?)parentId)], hierarchy.ChildClosed);
        Assert.Equal(1, series.InstancesClosed);
        Assert.Same(unblocking.Notices, notices);
    }

    [Fact]
    public async Task Reopening_blocks_successors_again_and_reopens_the_parent_count()
    {
        var parent = NewTask();
        var parentId = parent.Id;
        var task = NewSubtask(parent);
        var unblocking = new RecordingUnblocking();
        var hierarchy = new RecordingHierarchy();

        await TaskTransitionService.ApplyReopenedAsync(task, unblocking, hierarchy, default);

        Assert.Equal([(TenantId, task.Id)], unblocking.Reopened);
        Assert.Equal([(TenantId, (Guid?)parentId)], hierarchy.ChildReopened);
    }

    private static StartTaskCommand Command(Guid taskId, Guid byUserId, bool hasManageAll) =>
        new(TenantId, taskId, byUserId, hasManageAll);

    private sealed class RecordingUnblocking : ITaskUnblockingService
    {
        public List<(Guid, Guid)> Closed { get; } = [];
        public List<(Guid, Guid)> Reopened { get; } = [];
        public IReadOnlyList<TaskUnblockedIntegrationEvent> Notices { get; } = [];

        public Task<IReadOnlyList<TaskUnblockedIntegrationEvent>> ApplyPredecessorClosedAsync(
            Guid tenantId,
            Guid predecessorTaskId,
            CancellationToken ct = default
        )
        {
            Closed.Add((tenantId, predecessorTaskId));
            return Task.FromResult(Notices);
        }

        public Task ApplyPredecessorReopenedAsync(Guid tenantId, Guid predecessorTaskId, CancellationToken ct = default)
        {
            Reopened.Add((tenantId, predecessorTaskId));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHierarchy : ITaskHierarchyService
    {
        public List<(Guid, Guid?)> ChildClosed { get; } = [];
        public List<(Guid, Guid?)> ChildReopened { get; } = [];

        public Task ApplyChildClosedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default)
        {
            ChildClosed.Add((tenantId, parentTaskId));
            return Task.CompletedTask;
        }

        public Task ApplyChildReopenedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default)
        {
            ChildReopened.Add((tenantId, parentTaskId));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> DeleteWithDescendantsAsync(
            Guid tenantId,
            Guid taskId,
            Guid byUserId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Owner,
                Now
            )
            .Value;

    private static TaskItem NewSubtask(TaskItem parent) =>
        TaskItem
            .CreateSubtask(
                parent,
                Owner,
                TaskTitle.Create("Collect W-2").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                Owner,
                Now
            )
            .Value;
}
