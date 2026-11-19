using BuildingBlocks.Contracts.Work;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Application.Work;

namespace TaxVision.Tasks.Tests.Work;

/// <summary>Une tareas, pedidos al cliente y form requests en una sola respuesta del proveedor.</summary>
public sealed class WorkSummaryComposerTests
{
    private static readonly DateOnly Today = new(2026, 11, 18);

    [Fact]
    public void Top_puts_overdue_first_and_keeps_five()
    {
        var tasks = new TaskWork(
            [new WorkCount(WorkKinds.Tasks, 4, 1, 1, 0), new WorkCount(WorkKinds.ClientRequests, 1, 0, 0, 1)],
            [
                Item("no due", null, null),
                Item("in 3 days", Today.AddDays(3), null),
                Item("2 days late", Today.AddDays(-2), null),
            ]
        );
        WorkItem[] forms =
        [
            Item("timed today", null, new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc)),
            Item("tomorrow", Today.AddDays(1), null),
            Item("5 days late", Today.AddDays(-5), null),
        ];

        var summary = WorkSummaryComposer.Compose(tasks, new WorkCount(WorkKinds.FormRequests, 3, 1, 0, 2), forms);

        Assert.Equal("tasks", summary.Provider);
        Assert.Equal(
            [WorkKinds.Tasks, WorkKinds.ClientRequests, WorkKinds.FormRequests],
            summary.Counts.Select(c => c.Kind).ToArray()
        );
        Assert.Equal(
            ["5 days late", "2 days late", "timed today", "tomorrow", "in 3 days"],
            summary.Top.Select(i => i.Title).ToArray()
        );
    }

    [Fact]
    public void Without_client_forms_only_tasks_counts_are_returned()
    {
        var tasks = new TaskWork([new WorkCount(WorkKinds.Tasks, 0, 0, 0, 0)], []);

        var summary = WorkSummaryComposer.Compose(tasks, null, []);

        Assert.Equal([WorkKinds.Tasks], summary.Counts.Select(c => c.Kind).ToArray());
        Assert.Empty(summary.Top);
    }

    [Fact]
    public void Batch_sums_the_parts_and_returns_one_row_per_requested_user()
    {
        var ana = Guid.NewGuid();
        var luis = Guid.NewGuid();

        var batch = WorkSummaryComposer.Combine(
            [ana, luis],
            [new WorkBatchRow(ana, 3, 1)],
            [new WorkBatchRow(ana, 2, 2)]
        );

        Assert.Equal([new WorkBatchRow(ana, 5, 3), new WorkBatchRow(luis, 0, 0)], batch.Rows.ToArray());
    }

    [Fact]
    public void The_window_follows_WorkDue_in_the_office_zone()
    {
        var zone = ResolvedTimeZone.Resolve("America/New_York");

        var window = WorkWindow.For(new DateTime(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc), zone);

        Assert.Equal(new DateOnly(2026, 11, 17), window.Today);
        Assert.Equal(WorkDue.DueSoonEnd(window.Today), window.DueSoonEnd);
        Assert.Equal(LocalDay.StartUtc(new DateOnly(2026, 11, 24), zone), window.DueSoonEndUtc);
    }

    private static WorkItem Item(string title, DateOnly? dueDate, DateTime? dueAtUtc) =>
        new(Guid.NewGuid(), WorkKinds.Tasks, title, null, dueAtUtc, dueDate, "NotStarted", "/task?taskId=x");
}
