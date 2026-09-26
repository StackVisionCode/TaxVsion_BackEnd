using TaxVision.Tasks.Application.Dependencies.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Dependencies;

/// <summary>Una prueba por invariante D1–D5 más el efecto sobre el contador.</summary>
public sealed class AddDependencyHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Adding_a_dependency_blocks_the_successor()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var fixture = new Fixture(predecessor, successor);

        var result = await fixture.AddAsync(successor.Id, predecessor.Id);

        Assert.True(result.IsSuccess);
        Assert.True(successor.IsBlocked);
        Assert.Equal(1, fixture.Scope.CommitCount);
    }

    [Fact]
    public async Task A_closed_predecessor_does_not_raise_the_counter()
    {
        var predecessor = NewTask();
        predecessor.Complete(UserId, Now);
        var successor = NewTask();
        var fixture = new Fixture(predecessor, successor);

        var result = await fixture.AddAsync(successor.Id, predecessor.Id);

        Assert.True(result.IsSuccess);
        Assert.False(successor.IsBlocked);
    }

    [Fact]
    public async Task D1_a_task_cannot_depend_on_itself()
    {
        var task = NewTask();
        var fixture = new Fixture(task);

        var result = await fixture.AddAsync(task.Id, task.Id);

        Assert.Equal(TaskErrors.Dependency.SelfReference, result.Error);
    }

    [Fact]
    public async Task D2_a_task_from_another_tenant_is_rejected()
    {
        var successor = NewTask();
        var foreign = NewTask(OtherTenantId);
        var fixture = new Fixture(successor, foreign);

        var result = await fixture.AddAsync(successor.Id, foreign.Id);

        Assert.Equal(TaskErrors.Dependency.CrossTenant, result.Error);
    }

    [Fact]
    public async Task D3_the_same_edge_twice_is_rejected()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var fixture = new Fixture(predecessor, successor);
        await fixture.AddAsync(successor.Id, predecessor.Id);

        var result = await fixture.AddAsync(successor.Id, predecessor.Id);

        Assert.Equal(TaskErrors.Dependency.Duplicate, result.Error);
    }

    [Fact]
    public async Task D4_closing_a_cycle_is_rejected()
    {
        var a = NewTask();
        var b = NewTask();
        var c = NewTask();
        var fixture = new Fixture(a, b, c);
        await fixture.AddAsync(a.Id, b.Id);
        await fixture.AddAsync(b.Id, c.Id);

        var result = await fixture.AddAsync(c.Id, a.Id);

        Assert.Equal(TaskErrors.Dependency.Cycle, result.Error);
    }

    [Fact]
    public async Task Removing_a_dependency_releases_the_successor()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var fixture = new Fixture(predecessor, successor);
        await fixture.AddAsync(successor.Id, predecessor.Id);

        var result = await RemoveDependencyHandler.Handle(
            new RemoveDependencyCommand(TenantId, successor.Id, predecessor.Id, UserId),
            fixture.Tasks,
            fixture.Dependencies,
            fixture.Scope,
            fixture.UnitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(successor.IsBlocked);
    }

    [Fact]
    public async Task Removing_an_edge_that_is_not_there_reports_NotFound()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var fixture = new Fixture(predecessor, successor);

        var result = await RemoveDependencyHandler.Handle(
            new RemoveDependencyCommand(TenantId, successor.Id, predecessor.Id, UserId),
            fixture.Tasks,
            fixture.Dependencies,
            fixture.Scope,
            fixture.UnitOfWork,
            CancellationToken.None
        );

        Assert.Equal(TaskErrors.Dependency.NotFound, result.Error);
    }

    private sealed class Fixture(params TaskItem[] tasks)
    {
        public InMemoryTaskRepository Tasks { get; } = new(tasks);
        public InMemoryTaskDependencyRepository Dependencies { get; } = new();
        public ImmediateTransactionalScope Scope { get; } = new();
        public RecordingUnitOfWork UnitOfWork { get; } = new();

        public Task<BuildingBlocks.Results.Result> AddAsync(Guid taskId, Guid dependsOnTaskId) =>
            AddDependencyHandler.Handle(
                new AddDependencyCommand(TenantId, taskId, dependsOnTaskId, UserId),
                Tasks,
                Dependencies,
                Scope,
                UnitOfWork,
                new RecordingTaskMetrics(),
                CancellationToken.None
            );
    }

    // ---------------------------------------------------------------------
    // A1 — ownership: la dependencia cambia la SUCESORA, así que se valida sobre ella
    // ---------------------------------------------------------------------

    [Fact]
    public async Task A_stranger_cannot_block_someone_elses_task()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var fixture = new Fixture(predecessor, successor);

        var result = await AddDependencyHandler.Handle(
            new AddDependencyCommand(TenantId, successor.Id, predecessor.Id, Guid.NewGuid()),
            fixture.Tasks,
            fixture.Dependencies,
            fixture.Scope,
            fixture.UnitOfWork,
            new RecordingTaskMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Forbidden", result.Error.Code);
        Assert.False(successor.IsBlocked);
    }

    [Fact]
    public async Task A_stranger_cannot_unblock_someone_elses_task()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var fixture = new Fixture(predecessor, successor);
        await fixture.AddAsync(successor.Id, predecessor.Id);

        var result = await RemoveDependencyHandler.Handle(
            new RemoveDependencyCommand(TenantId, successor.Id, predecessor.Id, Guid.NewGuid()),
            fixture.Tasks,
            fixture.Dependencies,
            fixture.Scope,
            fixture.UnitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Forbidden", result.Error.Code);
        // Sigue bloqueada: el guard corre ANTES de quitar el borde.
        Assert.True(successor.IsBlocked);
    }

    /// <summary>El override de supervisión (tasks.manage_all) sí puede: es el caso del preparador
    /// senior reorganizando el trabajo del equipo.</summary>
    [Fact]
    public async Task The_supervision_override_can_block_a_task_it_does_not_own()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        var fixture = new Fixture(predecessor, successor);

        var result = await AddDependencyHandler.Handle(
            new AddDependencyCommand(TenantId, successor.Id, predecessor.Id, Guid.NewGuid(), HasManageAll: true),
            fixture.Tasks,
            fixture.Dependencies,
            fixture.Scope,
            fixture.UnitOfWork,
            new RecordingTaskMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.True(successor.IsBlocked);
    }

    private static TaskItem NewTask(Guid? tenantId = null) =>
        TaskItem
            .Create(
                tenantId ?? TenantId,
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
