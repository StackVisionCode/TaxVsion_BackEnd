using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Statuses.Commands;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>Estados de la oficina contra SQL real: archivado por lotes, concurrencia y filtro por código.</summary>
public sealed class WorkflowStatusIntegrationTests : IAsyncLifetime
{
    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Archiving_with_a_destination_moves_its_open_tasks_in_one_update()
    {
        var review = Status("Under review");
        var ready = Status("Ready to file");
        var open = TaskIn(review);
        var elsewhere = TaskIn(ready);
        await SeedAsync([review, ready], open, elsewhere);

        await using (var db = _database.NewContext(_tenantId))
        {
            var result = await ArchiveWorkflowStatusHandler.Handle(
                new ArchiveWorkflowStatusCommand(_tenantId, _admin, "under_review", "ready_to_file"),
                new WorkflowStatusRepository(db),
                db,
                default
            );
            Assert.True(result.IsSuccess);
            await db.SaveChangesAsync();
        }

        await using var check = _database.NewContext(_tenantId);
        Assert.Equal(ready.Id, (await check.Tasks.SingleAsync(t => t.Id == open.Id)).WorkflowStatusId);
        Assert.Equal(ready.Id, (await check.Tasks.SingleAsync(t => t.Id == elsewhere.Id)).WorkflowStatusId);
        Assert.True((await check.WorkflowStatuses.SingleAsync(s => s.Id == review.Id)).IsArchived);
    }

    [Fact]
    public async Task The_second_of_two_simultaneous_edits_is_rejected()
    {
        var review = Status("Under review");
        await SeedAsync([review]);
        await using var first = _database.NewContext(_tenantId);
        await using var second = _database.NewContext(_tenantId);
        var mine = await first.WorkflowStatuses.SingleAsync(s => s.Id == review.Id);
        var theirs = await second.WorkflowStatuses.SingleAsync(s => s.Id == review.Id);

        mine.Rename("Partner review", _admin, DateTime.UtcNow);
        await first.SaveChangesAsync();
        theirs.Rename("Manager review", _admin, DateTime.UtcNow);

        await Assert.ThrowsAsync<BuildingBlocks.Results.ConflictException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Searching_by_the_default_code_includes_tasks_without_their_own_status()
    {
        var review = Status("Under review");
        var explicitTask = TaskIn(review);
        var plain = NewTask();
        await SeedAsync([review], explicitTask, plain);

        await using var db = _database.NewContext(_tenantId);
        var page = await SearchTasksHandler.Handle(
            new SearchTasksQuery(_tenantId, new TaskQueryFilter(), 1, 20, _admin, true, StatusCode: "not_started"),
            new TaskRepository(db),
            new WorkflowStatusRepository(db),
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions()),
            default
        );
        var byCode = await SearchTasksHandler.Handle(
            new SearchTasksQuery(_tenantId, new TaskQueryFilter(), 1, 20, _admin, true, StatusCode: "under_review"),
            new TaskRepository(db),
            new WorkflowStatusRepository(db),
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions()),
            default
        );

        Assert.Equal(plain.Id, Assert.Single(page.Items).Id);
        Assert.Equal(explicitTask.Id, Assert.Single(byCode.Items).Id);
    }

    [Fact]
    public async Task The_open_task_tally_groups_by_stage_and_stored_code_inside_the_tenant()
    {
        var review = Status("Under review");
        var closed = TaskIn(review);
        closed.Complete(_admin, DateTime.UtcNow);
        await SeedAsync([review], TaskIn(review), TaskIn(review), NewTask(), closed);

        await using var db = _database.NewContext(_tenantId);
        var tallies = await new WorkflowStatusRepository(db).CountOpenTasksByCodeAsync(_tenantId);
        var other = await new WorkflowStatusRepository(db).CountOpenTasksByCodeAsync(Guid.NewGuid());

        Assert.Equal(2, tallies.Single(t => t.Code == "under_review").Count);
        Assert.Equal(1, tallies.Single(t => t.Code is null && t.Category == TaskItemStatus.NotStarted).Count);
        Assert.Empty(other);
    }

    private async Task SeedAsync(WorkflowStatus[] statuses, params TaskItem[] tasks)
    {
        await using var db = _database.NewContext(_tenantId);
        db.WorkflowStatuses.AddRange(statuses);
        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync();
    }

    private WorkflowStatus Status(string name) =>
        WorkflowStatus
            .CreateCustom(
                _tenantId,
                TaskLabelCode.FromDisplayName(name, new HashSet<string>()),
                name,
                LabelColor.Create("#2563EB").Value,
                TaskItemStatus.InProgress,
                10,
                null,
                _admin,
                DateTime.UtcNow
            )
            .Value;

    private TaskItem TaskIn(WorkflowStatus status)
    {
        var task = NewTask();
        task.MoveTo(status.ToRef(), TaskMoveRequest.None, _admin, DateTime.UtcNow);
        return task;
    }

    private TaskItem NewTask() =>
        TaskItem
            .Create(
                _tenantId,
                _admin,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                _admin,
                DateTime.UtcNow
            )
            .Value;
}
