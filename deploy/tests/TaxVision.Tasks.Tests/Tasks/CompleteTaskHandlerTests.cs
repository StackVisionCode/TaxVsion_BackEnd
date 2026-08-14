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

    private sealed class Fixture(TaskItem task)
    {
        public InMemoryTaskRepository Tasks { get; } = new(task);
        public RecordingUnblockingService Unblocking { get; } = new();
        public RecordingHierarchyService Hierarchy { get; } = new();
        public RecordingSeriesMaterializer SeriesMaterializer { get; } = new();
        public RecordingUnitOfWork UnitOfWork { get; } = new();
        public FakeMessageBus Bus { get; } = new();

        public Task<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>> CompleteAsync(
            Guid byUserId,
            bool hasManageAll
        ) =>
            CompleteTaskHandler.Handle(
                new CompleteTaskCommand(TenantId, task.Id, byUserId, hasManageAll),
                Tasks,
                Unblocking,
                Hierarchy,
                SeriesMaterializer,
                UnitOfWork,
                Bus,
                new NoOpCorrelationContext(),
                new RecordingTaskMetrics(),
                CancellationToken.None
            );
    }

    private sealed class RecordingUnblockingService : ITaskUnblockingService
    {
        public int ClosedCalls { get; private set; }

        public Task ApplyPredecessorClosedAsync(Guid tenantId, Guid predecessorTaskId, CancellationToken ct = default)
        {
            ClosedCalls++;
            return Task.CompletedTask;
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

        public Task DeleteWithDescendantsAsync(
            Guid tenantId,
            Guid taskId,
            Guid byUserId,
            CancellationToken ct = default
        ) => Task.CompletedTask;
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
