using TaxVision.Tasks.Application.Dependencies;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Dependencies;

public sealed class TaskUnblockingServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Closing_the_only_predecessor_unblocks_the_successor_and_returns_the_notice()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var service = Build(predecessor, successor, blockersOnSuccessor: 1);

        var unblocked = await service.ApplyPredecessorClosedAsync(TenantId, predecessor.Id, CancellationToken.None);

        Assert.False(successor.IsBlocked);
        var notice = Assert.Single(unblocked);
        Assert.Equal(successor.Id, notice.TaskId);
        Assert.Equal(TenantId, notice.TenantId);
    }

    [Fact]
    public async Task Closing_one_of_two_predecessors_leaves_the_successor_blocked_and_silent()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var service = Build(predecessor, successor, blockersOnSuccessor: 2);

        var unblocked = await service.ApplyPredecessorClosedAsync(TenantId, predecessor.Id, CancellationToken.None);

        Assert.True(successor.IsBlocked);
        Assert.Empty(unblocked);
    }

    [Fact]
    public async Task Reopening_a_predecessor_blocks_the_successor_again()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var service = Build(predecessor, successor, blockersOnSuccessor: 1);
        await service.ApplyPredecessorClosedAsync(TenantId, predecessor.Id, CancellationToken.None);

        await service.ApplyPredecessorReopenedAsync(TenantId, predecessor.Id, CancellationToken.None);

        Assert.True(successor.IsBlocked);
    }

    [Fact]
    public async Task Reprocessing_the_same_closure_does_not_announce_twice()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var service = Build(predecessor, successor, blockersOnSuccessor: 1);

        var first = await service.ApplyPredecessorClosedAsync(TenantId, predecessor.Id, CancellationToken.None);
        var second = await service.ApplyPredecessorClosedAsync(TenantId, predecessor.Id, CancellationToken.None);

        Assert.Single(first);
        Assert.Empty(second);
    }

    private static TaskUnblockingService Build(TaskItem predecessor, TaskItem successor, int blockersOnSuccessor)
    {
        for (var i = 0; i < blockersOnSuccessor; i++)
            successor.RegisterBlockerAdded();

        var dependencies = new InMemoryTaskDependencyRepository();
        dependencies.Seed(TenantId, successor.Id, predecessor.Id);

        return new TaskUnblockingService(
            new InMemoryTaskRepository(predecessor, successor),
            dependencies,
            new NoOpCorrelationContext()
        );
    }

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                UserId,
                TaskTitle.Create("Preparar 1040 de Pérez").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                UserId,
                Now
            )
            .Value;
}
