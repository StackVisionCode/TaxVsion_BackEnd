using BuildingBlocks.Messaging.TasksIntegrationEvents;
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
    public async Task Closing_the_only_predecessor_unblocks_the_successor_and_publishes()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var (service, bus) = Build(predecessor, successor, blockersOnSuccessor: 1);

        await service.ApplyPredecessorClosedAsync(TenantId, predecessor.Id, CancellationToken.None);

        Assert.False(successor.IsBlocked);
        var published = Assert.Single(bus.Published.OfType<TaskUnblockedIntegrationEvent>());
        Assert.Equal(successor.Id, published.TaskId);
        Assert.Equal(TenantId, published.TenantId);
    }

    [Fact]
    public async Task Closing_one_of_two_predecessors_leaves_the_successor_blocked_and_silent()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var (service, bus) = Build(predecessor, successor, blockersOnSuccessor: 2);

        await service.ApplyPredecessorClosedAsync(TenantId, predecessor.Id, CancellationToken.None);

        Assert.True(successor.IsBlocked);
        Assert.Empty(bus.Published.OfType<TaskUnblockedIntegrationEvent>());
    }

    [Fact]
    public async Task Reopening_a_predecessor_blocks_the_successor_again()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var (service, _) = Build(predecessor, successor, blockersOnSuccessor: 1);
        await service.ApplyPredecessorClosedAsync(TenantId, predecessor.Id, CancellationToken.None);

        await service.ApplyPredecessorReopenedAsync(TenantId, predecessor.Id, CancellationToken.None);

        Assert.True(successor.IsBlocked);
    }

    [Fact]
    public async Task Reprocessing_the_same_closure_does_not_publish_twice()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var (service, bus) = Build(predecessor, successor, blockersOnSuccessor: 1);

        await service.ApplyPredecessorClosedAsync(TenantId, predecessor.Id, CancellationToken.None);
        await service.ApplyPredecessorClosedAsync(TenantId, predecessor.Id, CancellationToken.None);

        Assert.Single(bus.Published.OfType<TaskUnblockedIntegrationEvent>());
    }

    private static (TaskUnblockingService Service, FakeMessageBus Bus) Build(
        TaskItem predecessor,
        TaskItem successor,
        int blockersOnSuccessor
    )
    {
        for (var i = 0; i < blockersOnSuccessor; i++)
            successor.RegisterBlockerAdded();

        var dependencies = new InMemoryTaskDependencyRepository();
        dependencies.Seed(TenantId, successor.Id, predecessor.Id);

        var bus = new FakeMessageBus();
        var service = new TaskUnblockingService(
            new InMemoryTaskRepository(predecessor, successor),
            dependencies,
            bus,
            new NoOpCorrelationContext()
        );
        return (service, bus);
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
