using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>Tablero por columnas: una por estado activo, con total real, 20 tarjetas y "Load more".</summary>
public sealed class GetTaskBoardColumnsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_office_without_its_own_statuses_gets_the_four_columns_of_always()
    {
        await using var db = NewContext();

        var board = await ColumnsAsync(db, new TaskQueryFilter(RootOnly: true));

        Assert.Equal(
            ["not_started", "in_progress", "waiting_on_client", "completed"],
            board.Columns.Select(c => c.Code)
        );
        Assert.All(board.Columns, c => Assert.Equal(0, c.Total));
    }

    [Fact]
    public async Task Each_status_has_its_own_column_and_tasks_without_one_go_to_the_default()
    {
        await using var db = NewContext();
        var review = Status("Under review", TaskItemStatus.InProgress, 20);
        var old = Status("Old step", TaskItemStatus.InProgress, 30);
        db.WorkflowStatuses.AddRange(review, old);
        db.Tasks.AddRange(In(review), In(review), In(old), Started(), Plain());
        await db.SaveChangesAsync();
        old.Archive(Owner, Now);
        await db.SaveChangesAsync();

        var board = await ColumnsAsync(db, new TaskQueryFilter(RootOnly: true));

        Assert.Equal(
            ["not_started", "in_progress", "under_review", "waiting_on_client", "completed"],
            board.Columns.Select(c => c.Code)
        );
        Assert.Equal(2, Column(board, "under_review").Total);
        // La del estado archivado y la que no tiene estado propio caen en el por defecto de su etapa.
        Assert.Equal(2, Column(board, "in_progress").Total);
        Assert.Equal(2, Column(board, "in_progress").Items!.Count);
        Assert.Equal(1, Column(board, "not_started").Total);
    }

    [Fact]
    public async Task A_column_brings_twenty_cards_its_real_total_and_a_cursor_for_the_rest()
    {
        await using var db = NewContext();
        db.Tasks.AddRange(Enumerable.Range(0, 27).Select(_ => Plain()));
        await db.SaveChangesAsync();

        var board = await ColumnsAsync(db, new TaskQueryFilter(RootOnly: true));
        var first = Column(board, "not_started");
        var more = await GetTaskBoardColumnHandler.Handle(
            new GetTaskBoardColumnQuery(
                TenantId,
                new TaskQueryFilter(RootOnly: true),
                "not_started",
                20,
                20,
                TaskBoardSort.Due,
                Owner,
                true
            ),
            new TaskBoardReader(db),
            new InMemoryWorkflowStatusRepository([.. db.WorkflowStatuses.IgnoreQueryFilters()]),
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions()),
            default
        );

        Assert.Equal(27, first.Total);
        Assert.Equal(20, first.Items!.Count);
        Assert.Equal("20", first.NextCursor);
        Assert.Equal(7, more.Value.Items!.Count);
        Assert.Null(more.Value.NextCursor);
        Assert.Empty(first.Items.Select(c => c.Id).Intersect(more.Value.Items.Select(c => c.Id)));
    }

    [Fact]
    public async Task Only_expanded_columns_bring_cards_but_every_column_brings_its_total()
    {
        await using var db = NewContext();
        db.Tasks.AddRange(Plain(), Started());
        await db.SaveChangesAsync();

        var board = await ColumnsAsync(db, new TaskQueryFilter(RootOnly: true), new HashSet<string> { "in_progress" });

        Assert.Null(Column(board, "not_started").Items);
        Assert.Equal(1, Column(board, "not_started").Total);
        Assert.Single(Column(board, "in_progress").Items!);
    }

    [Fact]
    public async Task Subtasks_are_not_cards_and_priority_filters_in_the_server()
    {
        await using var db = NewContext();
        var parent = Plain();
        var urgent = Plain(TaskPriority.Urgent);
        var child = TaskItem
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
        db.Tasks.AddRange(parent, urgent, child);
        await db.SaveChangesAsync();

        var roots = await ColumnsAsync(db, new TaskQueryFilter(RootOnly: true));
        var onlyUrgent = await ColumnsAsync(db, new TaskQueryFilter(RootOnly: true, Priority: TaskPriority.Urgent));

        Assert.Equal(2, Column(roots, "not_started").Total);
        var card = Column(roots, "not_started").Items!.Single(c => c.Id == parent.Id);
        Assert.Equal((1, 1), (card.OpenSubtaskCount, card.SubtaskCount));
        Assert.Equal(urgent.Id, Assert.Single(Column(onlyUrgent, "not_started").Items!).Id);
    }

    [Fact]
    public async Task An_unknown_column_is_not_found()
    {
        await using var db = NewContext();

        var result = await GetTaskBoardColumnHandler.Handle(
            new GetTaskBoardColumnQuery(
                TenantId,
                new TaskQueryFilter(RootOnly: true),
                "nope",
                0,
                20,
                TaskBoardSort.Due,
                Owner,
                true
            ),
            new TaskBoardReader(db),
            new InMemoryWorkflowStatusRepository(),
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions()),
            default
        );

        Assert.Equal("Task.Status.NotFound", result.Error.Code);
    }

    private static Task<TaskBoardColumnsResponse> ColumnsAsync(
        TasksDbContext db,
        TaskQueryFilter filter,
        IReadOnlySet<string>? expanded = null
    ) =>
        GetTaskBoardColumnsHandler.Handle(
            new GetTaskBoardColumnsQuery(TenantId, filter, expanded, 20, TaskBoardSort.Due, Owner, true),
            new TaskBoardReader(db),
            new InMemoryWorkflowStatusRepository([.. db.WorkflowStatuses.IgnoreQueryFilters()]),
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions()),
            default
        );

    private static TaskBoardColumnSlice Column(TaskBoardColumnsResponse board, string code) =>
        board.Columns.Single(c => c.Code == code);

    private static WorkflowStatus Status(string name, TaskItemStatus category, int order) =>
        WorkflowStatus
            .CreateCustom(
                TenantId,
                TaskLabelCode.FromDisplayName(name, new HashSet<string>()),
                name,
                LabelColor.Create("#2563EB").Value,
                category,
                order,
                null,
                Owner,
                Now
            )
            .Value;

    private static TaskItem Plain(TaskPriority priority = TaskPriority.Normal) =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                priority,
                TaskReference.None,
                null,
                null,
                Owner,
                Now
            )
            .Value;

    private static TaskItem Started()
    {
        var task = Plain();
        task.Start(Owner, Now);
        return task;
    }

    private static TaskItem In(WorkflowStatus status)
    {
        var task = Plain();
        task.MoveTo(status.ToRef(), TaskMoveRequest.None, Owner, Now);
        return task;
    }

    private static TasksDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
