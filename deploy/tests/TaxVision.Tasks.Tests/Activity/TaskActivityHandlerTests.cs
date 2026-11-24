using TaxVision.Tasks.Application.Activity;
using TaxVision.Tasks.Domain.Activity;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Activity;

/// <summary>Cada cambio que importa deja una entrada con su autor; los días van en días de la oficina.</summary>
public sealed class TaskActivityHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_assignment_records_who_assigned_whom()
    {
        var (activities, tasks, task) = Setup();

        await TaskActivityHandler.Handle(
            new TaskAssignedDomainEvent(task.Id, TenantId, Beto, Ana, Ana, Now, 0),
            activities,
            new RecordingUnitOfWork(),
            CancellationToken.None
        );

        var entry = Assert.Single(activities.Stored);
        Assert.Equal((TaskActivityKind.Assigned, Ana, Now), (entry.Kind, entry.ActorUserId, entry.OccurredAtUtc));
        var data = TaskActivityData.Read(entry.DataJson);
        Assert.Equal(Beto.ToString(), data["assigneeUserId"]);
        Assert.Equal(Ana.ToString(), data["previousAssigneeUserId"]);
    }

    [Fact]
    public async Task A_statutory_due_change_keeps_the_office_days_and_the_reason()
    {
        var (activities, _, task) = Setup();

        await TaskActivityHandler.Handle(
            new TaskDueChangedDomainEvent(
                task.Id,
                TenantId,
                new DateTime(2027, 4, 16, 3, 59, 59, DateTimeKind.Utc),
                new DateTime(2027, 10, 16, 3, 59, 59, DateTimeKind.Utc),
                "America/New_York",
                Ana,
                Now,
                "Extension filed (Form 4868)"
            ),
            activities,
            new RecordingUnitOfWork(),
            CancellationToken.None
        );

        var data = TaskActivityData.Read(Assert.Single(activities.Stored).DataJson);
        Assert.Equal("2027-04-15", data["previousDueOn"]);
        Assert.Equal("2027-10-15", data["dueOn"]);
        Assert.Equal("Extension filed (Form 4868)", data["reason"]);
    }

    [Fact]
    public async Task A_cancellation_keeps_its_reason()
    {
        var (activities, _, task) = Setup();

        await TaskActivityHandler.Handle(
            new TaskCancelledDomainEvent(task.Id, TenantId, null, null, "Client went elsewhere", Ana, Now),
            activities,
            new RecordingUnitOfWork(),
            CancellationToken.None
        );

        var entry = Assert.Single(activities.Stored);
        Assert.Equal(TaskActivityKind.Cancelled, entry.Kind);
        Assert.Equal("Client went elsewhere", TaskActivityData.Read(entry.DataJson)["reason"]);
    }

    [Fact]
    public async Task A_new_subtask_shows_on_its_parent_and_a_new_task_on_itself()
    {
        var (activities, tasks, parent) = Setup();
        var child = NewTask("Collect W-2");
        tasks.Add(child);

        await TaskActivityHandler.Handle(
            new TaskCreatedDomainEvent(child.Id, TenantId, Ana, null, parent.Id, Now),
            activities,
            tasks,
            new RecordingUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(2, activities.Stored.Count);
        var created = activities.Stored.Single(a => a.TaskId == child.Id);
        var added = activities.Stored.Single(a => a.TaskId == parent.Id);
        Assert.Equal(TaskActivityKind.Created, created.Kind);
        Assert.Equal(TaskActivityKind.SubtaskAdded, added.Kind);
        Assert.Equal("Collect W-2", TaskActivityData.Read(added.DataJson)["title"]);
    }

    [Fact]
    public async Task Correcting_and_removing_time_is_recorded_with_the_hours()
    {
        var (activities, _, task) = Setup();
        var entryId = Guid.NewGuid();

        await TaskActivityHandler.Handle(
            new TaskTimeEntryChangedDomainEvent(task.Id, TenantId, entryId, Beto, 1.5m, 1m, Ana, Now),
            activities,
            new RecordingUnitOfWork(),
            CancellationToken.None
        );
        await TaskActivityHandler.Handle(
            new TaskTimeEntryChangedDomainEvent(task.Id, TenantId, entryId, Beto, 1m, null, Ana, Now.AddMinutes(1)),
            activities,
            new RecordingUnitOfWork(),
            CancellationToken.None
        );

        Assert.Equal(
            [TaskActivityKind.TimeEntryEdited, TaskActivityKind.TimeEntryRemoved],
            activities.Stored.Select(a => a.Kind)
        );
        var edited = TaskActivityData.Read(activities.Stored[0].DataJson);
        Assert.Equal(("1.5", "1"), (edited["previousHours"], edited["hours"]));
        Assert.Equal(Beto.ToString(), edited["entryUserId"]);
    }

    [Fact]
    public async Task A_dependency_records_the_other_task_title()
    {
        var (activities, tasks, task) = Setup();
        var blocker = NewTask("Review documents");
        tasks.Add(blocker);

        await TaskActivityHandler.Handle(
            new TaskDependencyChangedDomainEvent(task.Id, TenantId, blocker.Id, true, Ana, Now),
            activities,
            tasks,
            new RecordingUnitOfWork(),
            CancellationToken.None
        );

        var entry = Assert.Single(activities.Stored);
        Assert.Equal(TaskActivityKind.DependencyAdded, entry.Kind);
        Assert.Equal("Review documents", TaskActivityData.Read(entry.DataJson)["title"]);
    }

    private static (InMemoryTaskActivityRepository, InMemoryTaskRepository, TaskItem) Setup()
    {
        var task = NewTask("Prepare 1040");
        return (new InMemoryTaskActivityRepository(), new InMemoryTaskRepository(task), task);
    }

    private static TaskItem NewTask(string title) =>
        TaskItem
            .Create(
                TenantId,
                Ana,
                TaskTitle.Create(title).Value,
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
