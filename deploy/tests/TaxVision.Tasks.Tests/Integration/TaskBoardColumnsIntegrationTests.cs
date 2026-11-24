using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Queries;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;
using Xunit.Abstractions;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>Tablero por columnas contra SQL real: traducción de las consultas, paginación y una medición con volumen.</summary>
public sealed class TaskBoardColumnsIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task A_column_of_137_says_137_brings_20_and_loads_the_rest_without_repeating()
    {
        await SeedAsync(Enumerable.Range(0, 137).Select(i => Task($"Return {i}", dueInDays: i % 40)));

        await using var db = _database.NewContext(_tenantId);
        var board = await ColumnsAsync(db, new TaskQueryFilter(RootOnly: true));
        var column = board.Columns.Single(c => c.Code == "not_started");
        var seen = column.Items!.Select(c => c.Id).ToList();
        var cursor = column.NextCursor;
        while (cursor is not null)
        {
            var more = await MoreAsync(db, "not_started", int.Parse(cursor));
            seen.AddRange(more.Items!.Select(c => c.Id));
            cursor = more.NextCursor;
        }

        Assert.Equal(137, column.Total);
        Assert.Equal(20, column.Items!.Count);
        Assert.Equal(137, seen.Distinct().Count());
    }

    [Fact]
    public async Task The_card_carries_due_estimate_request_and_subtask_counts_from_sql()
    {
        var parent = Task("Prepare 1040", dueInDays: 3, estimate: 2.5m);
        var child = TaskItem
            .CreateSubtask(
                parent,
                _admin,
                TaskTitle.Create("Collect W-2").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                _admin,
                Now
            )
            .Value;
        await SeedAsync([parent, child]);

        await using var db = _database.NewContext(_tenantId);
        var card = Assert.Single(
            (await ColumnsAsync(db, new TaskQueryFilter(RootOnly: true)))
                .Columns.Single(c => c.Code == "not_started")
                .Items!
        );

        Assert.Equal(parent.Id, card.Id);
        Assert.Equal(2.5m, card.EstimatedHours);
        Assert.Equal((1, 1), (card.OpenSubtaskCount, card.SubtaskCount));
        Assert.Equal(parent.Due!.DueOn, card.DueOn);
    }

    [Fact]
    public async Task Five_thousand_tasks_bring_every_total_and_twenty_cards_per_column()
    {
        var statuses = new[] { TaskItemStatus.NotStarted, TaskItemStatus.InProgress, TaskItemStatus.Completed };
        await SeedAsync(
            Enumerable
                .Range(0, 5000)
                .Select(i =>
                {
                    var task = Task($"Return {i}", dueInDays: i % 90);
                    if (statuses[i % 3] == TaskItemStatus.InProgress)
                        task.Start(_admin, Now);
                    if (statuses[i % 3] == TaskItemStatus.Completed)
                        task.Complete(_admin, Now);
                    return task;
                })
        );
        await using (var db = _database.NewContext(_tenantId))
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IX_Tasks_Board ON Tasks (TenantId, Status, WorkflowStatusCode, DueAtUtc)
                    INCLUDE (Title, Priority, AssigneeUserId, CreatedByUserId, CustomerId, TaxYear, DueTimeZoneId,
                             DueIsStatutory, OpenBlockerCount, OpenSubtaskCount, SubtaskCount, CompletedAtUtc, SeriesId,
                             Estimated, ExpectedItems)
                    WHERE ParentTaskId IS NULL;
                """
            );

        // Medido el 2026-11-24 contra GET /tasks/board antes de retirarlo: 39 ms el viejo, 19 ms este.
        await NewBoardAsync();
        var fresh = await TimeAsync(NewBoardAsync);
        output.WriteLine($"Tablero por columnas (totales + 20 por columna): {fresh.TotalMilliseconds:F0} ms");

        await using var check = _database.NewContext(_tenantId);
        var board = await ColumnsAsync(check, new TaskQueryFilter(RootOnly: true));
        Assert.Equal(5000, board.Columns.Sum(c => c.Total));
        Assert.All(board.Columns.Where(c => c.Total > 0), c => Assert.Equal(20, c.Items!.Count));
    }

    private async Task NewBoardAsync()
    {
        await using var db = _database.NewContext(_tenantId);
        await ColumnsAsync(db, new TaskQueryFilter(RootOnly: true));
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task> run)
    {
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 5; i++)
            await run();
        return watch.Elapsed / 5;
    }

    private Task<TaskBoardColumnsResponse> ColumnsAsync(
        Infrastructure.Persistence.TasksDbContext db,
        TaskQueryFilter filter
    ) =>
        GetTaskBoardColumnsHandler.Handle(
            new GetTaskBoardColumnsQuery(_tenantId, filter, null, 20, TaskBoardSort.Due, _admin, true),
            new TaskBoardReader(db),
            new WorkflowStatusRepository(db),
            new FixedOfficeZone("America/New_York"),
            Options.Create(new TasksVisibilityOptions()),
            default
        );

    private async Task<TaskBoardColumnSlice> MoreAsync(
        Infrastructure.Persistence.TasksDbContext db,
        string code,
        int skip
    ) =>
        (
            await GetTaskBoardColumnHandler.Handle(
                new GetTaskBoardColumnQuery(
                    _tenantId,
                    new TaskQueryFilter(RootOnly: true),
                    code,
                    skip,
                    20,
                    TaskBoardSort.Due,
                    _admin,
                    true
                ),
                new TaskBoardReader(db),
                new WorkflowStatusRepository(db),
                new FixedOfficeZone("America/New_York"),
                Options.Create(new TasksVisibilityOptions()),
                default
            )
        ).Value;

    private async Task SeedAsync(IEnumerable<TaskItem> tasks)
    {
        await using var db = _database.NewContext(_tenantId);
        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync();
    }

    private TaskItem Task(string title, int dueInDays, decimal? estimate = null) =>
        TaskItem
            .Create(
                _tenantId,
                _admin,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                DueDate.Create(Now.Date.AddDays(dueInDays), "America/New_York", false).Value,
                estimate is { } hours ? EstimatedHours.Create(hours).Value : null,
                _admin,
                Now
            )
            .Value;
}
