using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Dependencies;
using TaxVision.Tasks.Application.Hierarchy;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>D-6: cancelar una tarea cancela sus subtareas abiertas con el mismo motivo.</summary>
public sealed class CancelTaskHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Cancelling_a_task_cancels_its_open_subtasks_with_the_same_reason()
    {
        var parent = NewTask();
        var child = NewSubtask(parent, "Collect W-2");
        var grandchild = NewSubtask(child, "Scan W-2");
        var tree = new Tree(parent, child, grandchild);

        var result = await tree.CancelAsync(parent, "Client left");

        Assert.True(result.IsSuccess);
        Assert.All(new[] { parent, child, grandchild }, task => Assert.Equal(TaskItemStatus.Cancelled, task.Status));
        Assert.Equal("Client left", ReasonOf(grandchild));
        Assert.Equal(0, parent.OpenSubtaskCount);
        Assert.Equal(0, child.OpenSubtaskCount);
    }

    [Fact]
    public async Task A_completed_subtask_stays_completed()
    {
        var parent = NewTask();
        var done = NewSubtask(parent, "Collect W-2");
        done.Complete(Owner, Now);
        parent.RegisterSubtaskClosed();
        var tree = new Tree(parent, done);

        var result = await tree.CancelAsync(parent, "Client left");

        Assert.True(result.IsSuccess);
        Assert.Equal(TaskItemStatus.Completed, done.Status);
    }

    [Fact]
    public async Task Without_a_reason_nothing_is_cancelled()
    {
        var parent = NewTask();
        var child = NewSubtask(parent, "Collect W-2");
        var tree = new Tree(parent, child);

        var result = await tree.CancelAsync(parent, " ");

        Assert.Equal(TaskErrors.CancellationReasonRequired, result.Error);
        Assert.Equal(TaskItemStatus.NotStarted, parent.Status);
        Assert.Equal(TaskItemStatus.NotStarted, child.Status);
    }

    private static string? ReasonOf(TaskItem task) =>
        task.DomainEvents.OfType<TaskCancelledDomainEvent>().SingleOrDefault()?.Reason;

    private sealed class Tree(params TaskItem[] seed)
    {
        private readonly InMemoryTaskRepository _tasks = new(seed);
        private readonly InMemoryTaskDependencyRepository _dependencies = new();

        public Task<Result<TaskResponse>> CancelAsync(TaskItem task, string reason) =>
            CancelTaskHandler.Handle(
                new CancelTaskCommand(TenantId, task.Id, Owner, false, reason),
                _tasks,
                new TaskUnblockingService(_tasks, _dependencies, new NoOpCorrelationContext()),
                new TaskHierarchyService(_tasks, _dependencies),
                new RecordingSeriesMaterializer(),
                new RecordingUnitOfWork(),
                new FakeMessageBus(),
                CancellationToken.None
            );
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
}
