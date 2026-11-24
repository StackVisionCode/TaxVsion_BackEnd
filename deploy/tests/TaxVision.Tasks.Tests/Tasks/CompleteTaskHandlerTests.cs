using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

public sealed class CompleteTaskHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Completing_runs_both_cascades_and_publishes()
    {
        var task = NewTask();
        var fixture = new Fixture(task);

        var result = await fixture.CompleteAsync(Owner, hasManageAll: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fixture.Unblocking.ClosedCalls);
        Assert.Equal(1, fixture.Hierarchy.ChildClosedCalls);
        Assert.Single(fixture.Bus.Published.OfType<TaskCompletedIntegrationEvent>());
    }

    /// <summary>
    /// Completar dos veces es <c>Success</c> en el dominio. Sin la guarda de «estaba abierta», el
    /// segundo intento volvería a descontar el contador del padre y a avisar del mismo cierre.
    /// </summary>
    [Fact]
    public async Task Completing_twice_does_not_run_the_cascades_again()
    {
        var task = NewTask();
        var fixture = new Fixture(task);

        await fixture.CompleteAsync(Owner, hasManageAll: false);
        var second = await fixture.CompleteAsync(Owner, hasManageAll: false);

        Assert.True(second.IsSuccess);
        Assert.Equal(1, fixture.Unblocking.ClosedCalls);
        Assert.Equal(1, fixture.Hierarchy.ChildClosedCalls);
        Assert.Single(fixture.Bus.Published.OfType<TaskCompletedIntegrationEvent>());
    }

    [Fact]
    public async Task Closing_someone_elses_task_needs_the_supervision_override()
    {
        var task = NewTask();
        var fixture = new Fixture(task);

        var denied = await fixture.CompleteAsync(Stranger, hasManageAll: false);
        var allowed = await fixture.CompleteAsync(Stranger, hasManageAll: true);

        Assert.Equal(TaskErrors.Forbidden, denied.Error);
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public async Task Without_the_option_open_subtasks_still_block_completion()
    {
        var parent = NewTask();
        var child = NewSubtask(parent, "Collect W-2");
        var tree = new Tree(parent, child);

        var result = await tree.CompleteAsync(parent, completeOpenSubtasks: false);

        Assert.Equal("Task.HasOpenSubtasks", result.Error.Code);
        Assert.Equal(TaskItemStatus.NotStarted, child.Status);
    }

    [Fact]
    public async Task Complete_all_closes_every_open_subtask_and_then_the_task()
    {
        var parent = NewTask();
        var child = NewSubtask(parent, "Collect W-2");
        var grandchild = NewSubtask(child, "Scan W-2");
        var dropped = NewSubtask(parent, "Old step");
        dropped.Cancel("Not needed", Owner, Now);
        parent.RegisterSubtaskClosed();
        var tree = new Tree(parent, child, grandchild, dropped);

        var result = await tree.CompleteAsync(parent, completeOpenSubtasks: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(TaskItemStatus.Completed, grandchild.Status);
        Assert.Equal(TaskItemStatus.Completed, child.Status);
        Assert.Equal(TaskItemStatus.Completed, parent.Status);
        Assert.Equal(TaskItemStatus.Cancelled, dropped.Status);
        Assert.Equal(0, parent.OpenSubtaskCount);
    }

    [Fact]
    public async Task Complete_all_with_a_blocked_subtask_changes_nothing_and_names_it()
    {
        var parent = NewTask();
        var free = NewSubtask(parent, "Collect W-2");
        var waiting = NewSubtask(parent, "Client signature");
        waiting.RegisterBlockerAdded();
        var tree = new Tree(parent, free, waiting);

        var result = await tree.CompleteAsync(parent, completeOpenSubtasks: true);

        Assert.Equal("Task.Subtask.Blocked", result.Error.Code);
        Assert.Contains("Client signature", result.Error.Message);
        Assert.Equal(TaskItemStatus.NotStarted, free.Status);
        Assert.Equal(TaskItemStatus.NotStarted, parent.Status);
        Assert.Equal(2, parent.OpenSubtaskCount);
    }

    /// <summary>Servicios reales de jerarquía y desbloqueo: el contador de la madre baja de verdad.</summary>
    private sealed class Tree(params TaskItem[] seed)
    {
        private readonly InMemoryTaskRepository _tasks = new(seed);
        private readonly InMemoryTaskDependencyRepository _dependencies = new();

        public Task<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>> CompleteAsync(
            TaskItem task,
            bool completeOpenSubtasks
        ) =>
            CompleteTaskHandler.Handle(
                new CompleteTaskCommand(TenantId, task.Id, Owner, false, completeOpenSubtasks),
                _tasks,
                new TaxVision.Tasks.Application.Dependencies.TaskUnblockingService(
                    _tasks,
                    _dependencies,
                    new NoOpCorrelationContext()
                ),
                new TaxVision.Tasks.Application.Hierarchy.TaskHierarchyService(_tasks, _dependencies),
                new RecordingSeriesMaterializer(),
                new RecordingUnitOfWork(),
                new FakeMessageBus(),
                new RecordingTaskMetrics(),
                CancellationToken.None
            );
    }

    private static TaskItem NewSubtask(TaskItem parent, string title) =>
        TaskItem
            .CreateSubtask(
                parent,
                Owner,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                Owner,
                Now
            )
            .Value;

    private sealed class Fixture(TaskItem task)
    {
        public InMemoryTaskRepository Tasks { get; } = new(task);
        public RecordingUnblockingService Unblocking { get; } = new();
        public RecordingHierarchyService Hierarchy { get; } = new();
        public RecordingSeriesMaterializer SeriesMaterializer { get; } = new();
        public RecordingUnitOfWork UnitOfWork { get; } = new();
        public FakeMessageBus Bus { get; } = new();

        // TaskCompleted sale del handler del evento de dominio (el replay lo deja en el bus).
        public async Task<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>> CompleteAsync(
            Guid byUserId,
            bool hasManageAll
        )
        {
            var result = await CompleteTaskHandler.Handle(
                new CompleteTaskCommand(TenantId, task.Id, byUserId, hasManageAll),
                Tasks,
                Unblocking,
                Hierarchy,
                SeriesMaterializer,
                UnitOfWork,
                Bus,
                new RecordingTaskMetrics(),
                CancellationToken.None
            );
            await DomainEventReplay.DispatchIntoAsync(Bus, Tasks);
            return result;
        }
    }

    private sealed class RecordingUnblockingService : ITaskUnblockingService
    {
        public int ClosedCalls { get; private set; }

        public Task<IReadOnlyList<TaskUnblockedIntegrationEvent>> ApplyPredecessorClosedAsync(
            Guid tenantId,
            Guid predecessorTaskId,
            CancellationToken ct = default
        )
        {
            ClosedCalls++;
            return Task.FromResult<IReadOnlyList<TaskUnblockedIntegrationEvent>>([]);
        }

        public Task ApplyPredecessorReopenedAsync(
            Guid tenantId,
            Guid predecessorTaskId,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class RecordingHierarchyService : ITaskHierarchyService
    {
        public int ChildClosedCalls { get; private set; }

        public Task ApplyChildClosedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default)
        {
            ChildClosedCalls++;
            return Task.CompletedTask;
        }

        public Task ApplyChildReopenedAsync(Guid tenantId, Guid? parentTaskId, CancellationToken ct = default) =>
            Task.CompletedTask;

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
                TaskTitle.Create("Preparar 1040 de Pérez").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Owner,
                Now
            )
            .Value;
}
