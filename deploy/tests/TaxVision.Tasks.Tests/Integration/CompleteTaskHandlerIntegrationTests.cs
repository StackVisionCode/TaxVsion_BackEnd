using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Dependencies;
using TaxVision.Tasks.Application.Hierarchy;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// "Complete all and finish" contra SQL real. El middleware de Wolverine guarda aunque la orden falle,
/// así que el rechazo tiene que dejar la base igual tras ese guardado.
/// </summary>
public sealed class CompleteTaskHandlerIntegrationTests : IAsyncLifetime
{
    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Complete_all_saves_the_subtasks_and_the_task_together()
    {
        var (parentId, childIds) = await SeedAsync(blockLastChild: false);

        var result = await CompleteAsync(parentId);

        Assert.True(result.IsSuccess);
        await using var db = _database.NewContext(_tenantId);
        var parent = await db.Tasks.SingleAsync(t => t.Id == parentId);
        Assert.Equal(TaskItemStatus.Completed, parent.Status);
        Assert.Equal(0, parent.OpenSubtaskCount);
        Assert.All(
            await db.Tasks.Where(t => childIds.Contains(t.Id)).ToListAsync(),
            child => Assert.Equal(TaskItemStatus.Completed, child.Status)
        );
    }

    [Fact]
    public async Task A_blocked_subtask_leaves_the_database_unchanged()
    {
        var (parentId, childIds) = await SeedAsync(blockLastChild: true);

        var result = await CompleteAsync(parentId);

        Assert.Equal("Task.Subtask.Blocked", result.Error.Code);
        await using var db = _database.NewContext(_tenantId);
        var parent = await db.Tasks.SingleAsync(t => t.Id == parentId);
        Assert.Equal(TaskItemStatus.NotStarted, parent.Status);
        Assert.Equal(2, parent.OpenSubtaskCount);
        Assert.All(
            await db.Tasks.Where(t => childIds.Contains(t.Id)).ToListAsync(),
            child => Assert.Equal(TaskItemStatus.NotStarted, child.Status)
        );
    }

    /// <summary>Como el middleware: el handler y después SaveChanges, pase lo que pase.</summary>
    private async Task<Result<TaskResponse>> CompleteAsync(Guid parentId)
    {
        await using var db = _database.NewContext(_tenantId);
        var tasks = new TaskRepository(db);
        var dependencies = new TaskDependencyRepository(db);
        var result = await CompleteTaskHandler.Handle(
            new CompleteTaskCommand(_tenantId, parentId, _owner, false, CompleteOpenSubtasks: true),
            tasks,
            new TaskUnblockingService(tasks, dependencies, new NoOpCorrelationContext()),
            new TaskHierarchyService(tasks, dependencies),
            new RecordingSeriesMaterializer(),
            db,
            new FakeMessageBus(),
            new RecordingTaskMetrics(),
            CancellationToken.None
        );
        await db.SaveChangesAsync();
        return result;
    }

    private async Task<(Guid ParentId, Guid[] ChildIds)> SeedAsync(bool blockLastChild)
    {
        var parent = TaskItem
            .Create(
                _tenantId,
                _owner,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                _owner,
                DateTime.UtcNow
            )
            .Value;
        var first = NewSubtask(parent, "Collect W-2");
        var last = NewSubtask(parent, "Client signature");
        if (blockLastChild)
            last.RegisterBlockerAdded();

        await using var db = _database.NewContext(_tenantId);
        db.Tasks.AddRange(parent, first, last);
        await db.SaveChangesAsync();
        return (parent.Id, [first.Id, last.Id]);
    }

    private TaskItem NewSubtask(TaskItem parent, string title) =>
        TaskItem
            .CreateSubtask(
                parent,
                _owner,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                _owner,
                DateTime.UtcNow
            )
            .Value;
}
