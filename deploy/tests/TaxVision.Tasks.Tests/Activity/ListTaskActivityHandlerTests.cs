using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Activity;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Activity;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Activity;

/// <summary>El historial de una tarea, de lo más nuevo a lo más viejo, por páginas y con la visibilidad de la tarea.</summary>
public sealed class ListTaskActivityHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_move_to_another_stage_is_one_entry_with_the_office_status_name()
    {
        var task = NewTask();
        var done = Status("filed", "Filed", TaskItemStatus.Completed);
        var activities = new InMemoryTaskActivityRepository(
            Entry(task, TaskActivityKind.Completed, Now, null),
            Entry(
                task,
                TaskActivityKind.StatusChanged,
                Now,
                new Dictionary<string, string?>
                {
                    ["toStatusId"] = done.Id.ToString(),
                    ["fromCategory"] = "InProgress",
                    ["toCategory"] = "Completed",
                }
            )
        );

        var page = await ListAsync(task, activities, new InMemoryWorkflowStatusRepository(done));

        var item = Assert.Single(page.Value.Items);
        Assert.Equal("Completed", item.Kind);
        Assert.Equal("Filed", item.Data["toStatusName"]);
    }

    [Fact]
    public async Task A_status_change_within_a_stage_names_both_statuses()
    {
        var task = NewTask();
        var review = Status("review", "In review", TaskItemStatus.InProgress);
        var activities = new InMemoryTaskActivityRepository(
            Entry(
                task,
                TaskActivityKind.StatusChanged,
                Now,
                new Dictionary<string, string?>
                {
                    ["fromStatusId"] = null,
                    ["toStatusId"] = review.Id.ToString(),
                    ["fromCategory"] = "InProgress",
                    ["toCategory"] = "InProgress",
                }
            )
        );

        var item = Assert.Single(
            (await ListAsync(task, activities, new InMemoryWorkflowStatusRepository(review))).Value.Items
        );

        Assert.Equal("StatusChanged", item.Kind);
        Assert.Equal("In review", item.Data["toStatusName"]);
    }

    [Fact]
    public async Task Pages_go_from_newest_to_oldest()
    {
        var task = NewTask();
        var activities = new InMemoryTaskActivityRepository([
            .. Enumerable
                .Range(0, 5)
                .Select(i => Entry(task, TaskActivityKind.PriorityChanged, Now.AddMinutes(i), null)),
        ]);

        var first = (await ListAsync(task, activities, new InMemoryWorkflowStatusRepository(), take: 3)).Value;
        var second = (
            await ListAsync(task, activities, new InMemoryWorkflowStatusRepository(), take: 3, before: first.NextBefore)
        ).Value;

        Assert.Equal(
            [Now.AddMinutes(4), Now.AddMinutes(3), Now.AddMinutes(2)],
            first.Items.Select(i => i.OccurredAtUtc)
        );
        Assert.Equal([Now.AddMinutes(1), Now], second.Items.Select(i => i.OccurredAtUtc));
        Assert.Null(second.NextBefore);
    }

    [Fact]
    public async Task Someone_who_cannot_see_the_task_cannot_see_its_history()
    {
        var task = NewTask();

        var result = await ListTaskActivityHandler.Handle(
            new ListTaskActivityQuery(TenantId, task.Id, Guid.NewGuid(), false, null, 50),
            new InMemoryTaskRepository(),
            new InMemoryTaskActivityRepository(Entry(task, TaskActivityKind.Assigned, Now, null)),
            new InMemoryWorkflowStatusRepository(),
            Options.Create(new TasksVisibilityOptions { Enabled = true }),
            CancellationToken.None
        );

        Assert.Equal(TaskErrors.NotFound, result.Error);
    }

    private static Task<BuildingBlocks.Results.Result<TaskActivityPage>> ListAsync(
        TaskItem task,
        InMemoryTaskActivityRepository activities,
        InMemoryWorkflowStatusRepository statuses,
        int take = 50,
        DateTime? before = null
    ) =>
        ListTaskActivityHandler.Handle(
            new ListTaskActivityQuery(TenantId, task.Id, Ana, true, before, take),
            new InMemoryTaskRepository(task),
            activities,
            statuses,
            Options.Create(new TasksVisibilityOptions()),
            CancellationToken.None
        );

    private static TaskActivity Entry(
        TaskItem task,
        TaskActivityKind kind,
        DateTime at,
        IReadOnlyDictionary<string, string?>? data
    ) => TaskActivity.Record(TenantId, task.Id, kind, Ana, at, TaskActivityData.Write(data));

    private static WorkflowStatus Status(string code, string name, TaskItemStatus category) =>
        WorkflowStatus
            .Create(TenantId, TaskLabelCode.Create(code).Value, name, LabelColor.Create("#6366F1").Value, category, 0)
            .Value;

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                Ana,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Ana,
                Now
            )
            .Value;
}
