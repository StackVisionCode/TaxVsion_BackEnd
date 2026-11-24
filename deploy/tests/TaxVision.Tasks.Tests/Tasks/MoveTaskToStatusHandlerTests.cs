using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Dependencies;
using TaxVision.Tasks.Application.Hierarchy;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Statuses;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;
using TaxVision.Tasks.Tests.Templates;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>El movimiento único: una llamada lleva la tarea a un estado de la oficina con la regla de su etapa.</summary>
public sealed class MoveTaskToStatusHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Moves_a_task_to_an_office_status_and_answers_with_its_code()
    {
        var review = Status("Under review", TaskItemStatus.InProgress);
        var task = NewTask();
        var fixture = new Fixture([review], task);

        var result = await fixture.MoveAsync(task, "under_review");

        Assert.Equal(TaskItemStatus.InProgress, result.Value.Status);
        Assert.Equal("under_review", result.Value.WorkflowStatusCode);
    }

    [Fact]
    public async Task A_status_of_another_office_is_not_found()
    {
        var theirs = Status("Under review", TaskItemStatus.InProgress, Guid.NewGuid());
        var task = NewTask();
        var fixture = new Fixture([theirs], task);

        var result = await fixture.MoveAsync(task, "under_review");

        Assert.Equal(TaskErrors.Status.NotFound, result.Error);
        Assert.Equal(TaskItemStatus.NotStarted, task.Status);
    }

    [Fact]
    public async Task An_archived_status_takes_no_tasks()
    {
        var review = Status("Under review", TaskItemStatus.InProgress);
        review.Archive(Owner, Now);
        var task = NewTask();

        var result = await new Fixture([review], task).MoveAsync(task, "under_review");

        Assert.Equal(TaskErrors.Status.Archived, result.Error);
    }

    [Fact]
    public async Task Completing_with_open_subtasks_keeps_the_task_where_it_was()
    {
        var parent = NewTask();
        var child = TaskItem
            .CreateSubtask(
                parent,
                Owner,
                TaskTitle.Create("Sign").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                Owner,
                Now
            )
            .Value;
        var fixture = new Fixture([], parent, child);

        var refused = await fixture.MoveAsync(parent, "completed");
        var all = await fixture.MoveAsync(parent, "completed", completeOpenSubtasks: true);

        Assert.Equal("Task.HasOpenSubtasks", refused.Error.Code);
        Assert.True(all.IsSuccess);
        Assert.Equal(TaskItemStatus.Completed, child.Status);
    }

    [Fact]
    public async Task Waiting_on_client_needs_a_client()
    {
        var task = NewTask();

        var result = await new Fixture([], task).MoveAsync(task, "waiting_on_client", expectedItems: "W-2");

        Assert.Equal(TaskErrors.WaitingOnClient.CustomerRequired, result.Error);
    }

    [Fact]
    public async Task A_subtask_is_not_reopened_under_a_closed_task()
    {
        var parent = NewTask();
        var child = TaskItem
            .CreateSubtask(
                parent,
                Owner,
                TaskTitle.Create("Sign").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                Owner,
                Now
            )
            .Value;
        child.Complete(Owner, Now);
        parent.RegisterSubtaskClosed();
        parent.Complete(Owner, Now);

        var result = await new Fixture([], parent, child).MoveAsync(child, "in_progress");

        Assert.Equal(TaskErrors.Subtask.ParentClosed, result.Error);
        Assert.Equal(TaskItemStatus.Completed, child.Status);
    }

    [Fact]
    public async Task Cancelling_through_a_move_keeps_the_reason()
    {
        var task = NewTask();

        var result = await new Fixture([], task).MoveAsync(task, "cancelled", cancelReason: "Client left");

        Assert.Equal(TaskItemStatus.Cancelled, result.Value.Status);
        Assert.Equal("Client left", result.Value.CancellationReason);
    }

    [Fact]
    public async Task Moving_an_occurrence_the_series_already_replaced_back_to_work_is_rejected()
    {
        var task = NewTask();
        task.Complete(Owner, Now);
        var fixture = new Fixture([], task);
        var series = SeriesOf(task);
        series.RegisterMaterialized(Guid.NewGuid(), series.PlanNextOccurrence(null, null, Now).Value);
        fixture.Series.Add(series);

        var result = await fixture.MoveAsync(task, "in_progress");

        Assert.Equal(TaskErrors.Series.OccurrenceSuperseded, result.Error);
        Assert.Equal(TaskItemStatus.Completed, task.Status);
    }

    [Fact]
    public async Task Moving_the_last_occurrence_of_a_paused_series_back_to_work_makes_it_the_open_one()
    {
        var task = NewTask();
        task.Complete(Owner, Now);
        var fixture = new Fixture([], task);
        var series = SeriesOf(task);
        series.Pause();
        fixture.Series.Add(series);

        var result = await fixture.MoveAsync(task, "in_progress");

        Assert.True(result.IsSuccess);
        Assert.Equal(task.Id, series.OpenInstanceId);
    }

    /// <summary>Serie mensual cuya primera ocurrencia es <paramref name="task"/>, ya cerrada.</summary>
    private static TaxVision.Tasks.Domain.Series.TaskSeries SeriesOf(TaskItem task)
    {
        var series = TaxVision
            .Tasks.Domain.Series.TaskSeries.Create(
                TenantId,
                Owner,
                RecurrenceRule.Create("FREQ=MONTHLY;BYMONTHDAY=15", "America/New_York").Value,
                TaxVision.Tasks.Domain.Series.RecurrenceMode.FixedSchedule,
                new TaxVision.Tasks.Domain.Series.TaskItemBlueprint
                {
                    Title = TaskTitle.Create("Payroll").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.None,
                    AssigneeUserId = Owner,
                    IsStatutory = false,
                },
                Now.AddDays(10),
                null,
                null,
                Now
            )
            .Value;
        task.AttachToSeries(series.Id, 1);
        series.RegisterMaterialized(task.Id, series.PlanNextOccurrence(null, null, Now).Value);
        series.RegisterInstanceClosed(task.Id);
        return series;
    }

    private sealed class Fixture(WorkflowStatus[] statuses, params TaskItem[] seed)
    {
        private readonly InMemoryTaskRepository _tasks = new(seed);
        public InMemoryTaskSeriesRepository Series { get; } = new();
        private readonly InMemoryTaskDependencyRepository _dependencies = new();
        private readonly InMemoryWorkflowStatusRepository _statuses = new(statuses);

        public Task<Result<TaskResponse>> MoveAsync(
            TaskItem task,
            string status,
            string? expectedItems = null,
            string? cancelReason = null,
            bool completeOpenSubtasks = false
        ) =>
            MoveTaskToStatusHandler.Handle(
                new MoveTaskToStatusCommand(
                    TenantId,
                    task.Id,
                    Owner,
                    false,
                    status,
                    expectedItems,
                    null,
                    cancelReason,
                    completeOpenSubtasks
                ),
                _tasks,
                _statuses,
                new TaskUnblockingService(_tasks, _dependencies, new NoOpCorrelationContext()),
                new TaskHierarchyService(_tasks, _dependencies),
                Series.Stored.Count > 0
                    ? new TaskSeriesMaterializer(_tasks, Series, new InMemoryTaskTemplateRepository())
                    : new RecordingSeriesMaterializer(),
                new RecordingUnitOfWork(),
                new FakeMessageBus(),
                new NoOpCorrelationContext(),
                new FixedOfficeZone("America/New_York"),
                CancellationToken.None
            );
    }

    private static WorkflowStatus Status(string name, TaskItemStatus category, Guid? tenantId = null) =>
        WorkflowStatus
            .CreateCustom(
                tenantId ?? TenantId,
                TaskLabelCode.FromDisplayName(name, new HashSet<string>()),
                name,
                LabelColor.Create("#2563EB").Value,
                category,
                10,
                null,
                Owner,
                Now
            )
            .Value;

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create("Prepare 1040").Value,
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
