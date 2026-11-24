using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Dependencies.Queries;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Dependencies;

/// <summary>"Waiting on" y "Blocking" en una sola consulta, con lo que el panel muestra de cada tarea.</summary>
public sealed class GetTaskRelationsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Lists_what_the_task_waits_on_and_what_it_blocks()
    {
        var review = NewTask(
            "Review",
            assignee: Ana,
            due: DueDate.Create(new DateTime(2026, 12, 1, 4, 59, 0, DateTimeKind.Utc), "America/New_York", false).Value
        );
        var file = NewTask("File");
        var notify = NewTask("Notify client");
        var fixture = new Fixture(review, file, notify);
        fixture.Edge(file, waitsOn: review);
        fixture.Edge(notify, waitsOn: file);

        var result = await fixture.RelationsOf(file);

        var waitingOn = Assert.Single(result.Value.WaitingOn);
        Assert.Equal(review.Id, waitingOn.TaskId);
        Assert.Equal("Review", waitingOn.Title);
        Assert.Equal(TaskItemStatus.NotStarted, waitingOn.Status);
        Assert.Equal(Ana, waitingOn.AssigneeUserId);
        Assert.Equal(new DateTime(2026, 12, 1, 4, 59, 0, DateTimeKind.Utc), waitingOn.DueAtUtc);
        Assert.Equal(notify.Id, Assert.Single(result.Value.Blocking).TaskId);
    }

    [Fact]
    public async Task A_task_the_actor_cannot_see_is_not_found()
    {
        var hidden = NewTask("Client task", customerId: Guid.NewGuid());
        var fixture = new Fixture(hidden);

        var result = await fixture.RelationsOf(hidden, actor: Ana);

        Assert.Equal(TaskErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Related_tasks_the_actor_cannot_see_are_left_out()
    {
        var mine = NewTask("Mine", assignee: Ana);
        var hidden = NewTask("Other client task", customerId: Guid.NewGuid());
        var fixture = new Fixture(mine, hidden);
        fixture.Edge(mine, waitsOn: hidden);

        var result = await fixture.RelationsOf(mine, actor: Ana);

        Assert.Empty(result.Value.WaitingOn);
    }

    private sealed class Fixture(params TaskItem[] seed)
    {
        private readonly InMemoryTaskRepository _tasks = new(seed);
        private readonly InMemoryTaskDependencyRepository _dependencies = new();

        public void Edge(TaskItem task, TaskItem waitsOn) =>
            _dependencies.Add(TaskDependency.Create(TenantId, task.Id, waitsOn.Id, Owner, Now).Value);

        public Task<BuildingBlocks.Results.Result<TaskRelationsResponse>> RelationsOf(
            TaskItem task,
            Guid? actor = null
        ) =>
            GetTaskRelationsHandler.Handle(
                new GetTaskRelationsQuery(TenantId, task.Id, actor ?? Owner, CanViewAll: actor is null),
                _tasks,
                _dependencies,
                Options.Create(new TasksVisibilityOptions { Enabled = true }),
                CancellationToken.None
            );
    }

    private static TaskItem NewTask(
        string title,
        Guid? assignee = null,
        DueDate? due = null,
        Guid? customerId = null
    ) =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create(title).Value,
                null,
                TaskPriority.Normal,
                customerId is { } id ? TaskReference.Create(id, null).Value : TaskReference.None,
                due,
                null,
                assignee,
                Now
            )
            .Value;
}
