using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>
/// La edición del panel en un solo guardado (D-27) con la versión de la tarea (D-18): todo o nada, y si otro la cambió
/// no se pisa.
/// </summary>
public sealed class UpdateTaskHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();
    private static readonly byte[] Version = [0, 0, 0, 0, 0, 0, 7, 209];

    [Fact]
    public async Task Every_change_of_the_form_is_saved_once_and_announced_after()
    {
        var task = NewTask();
        var fixture = new Fixture(task);

        var result = await fixture.RunAsync(
            Form(task) with
            {
                Title = "Prepare 1040-X",
                Priority = TaskPriority.High,
                DueOn = new DateOnly(2027, 4, 15),
                AssigneeUserId = Beto,
                EstimatedMinutes = 90,
            }
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(
            ("Prepare 1040-X", TaskPriority.High, new DateOnly(2027, 4, 15), (Guid?)Beto, 1.5m),
            (task.Title.Value, task.Priority, task.Due!.DueOn, task.AssigneeUserId, task.Estimated!.Value)
        );
        Assert.Contains(fixture.Bus.Published, m => m is TaskDueChangedIntegrationEvent);
        Assert.Contains(fixture.Bus.Published, m => m is ReminderTargetMovedIntegrationEvent);
        Assert.Equal(Version, fixture.Tasks.ExpectedVersions[task.Id]);
    }

    [Fact]
    public async Task Nothing_changed_saves_nothing_new_and_announces_nothing()
    {
        var task = NewTask();
        var fixture = new Fixture(task);

        var result = await fixture.RunAsync(Form(task));

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Bus.Published);
    }

    [Fact]
    public async Task A_form_read_before_someone_else_changed_the_task_is_rejected_whole()
    {
        var task = NewTask();
        var fixture = new Fixture(task);

        var result = await fixture.RunAsync(Form(task) with { Title = "Prepare 1040-X", RowVersion = "AAAAAAAAB9A=" });

        Assert.Equal(TaskErrors.StaleVersion, result.Error);
        Assert.Equal("Prepare 1040", task.Title.Value);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Changing_the_assignee_needs_the_assign_permission()
    {
        var task = NewTask();
        var fixture = new Fixture(task);

        var result = await fixture.RunAsync(Form(task) with { AssigneeUserId = Beto }, canAssign: false);

        Assert.Equal(TaskErrors.Forbidden, result.Error);
        Assert.Equal(Owner, task.AssigneeUserId);
    }

    [Fact]
    public async Task One_invalid_field_leaves_the_task_as_it_was()
    {
        var task = NewTask();
        var fixture = new Fixture(task);

        var result = await fixture.RunAsync(Form(task) with { Title = "Prepare 1040-X", EstimatedMinutes = -5 });

        Assert.True(result.IsFailure);
        Assert.Equal("Prepare 1040", task.Title.Value);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_new_customer_and_year_reach_the_whole_branch()
    {
        var task = NewTask();
        var child = TaskItem
            .CreateSubtask(
                task,
                Owner,
                TaskTitle.Create("Collect W-2").Value,
                null,
                TaskPriority.Normal,
                null,
                null,
                Owner,
                DateTime.UtcNow
            )
            .Value;
        var fixture = new Fixture(task, child);
        var acme = Guid.NewGuid();

        var result = await fixture.RunAsync(Form(task) with { CustomerId = acme, TaxYear = 2025 });

        Assert.True(result.IsSuccess);
        Assert.All(
            new[] { task, child },
            t => Assert.Equal(((Guid?)acme, (int?)2025), (t.Reference.CustomerId, t.Reference.TaxYear))
        );
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    private static UpdateTaskCommand Form(TaskItem task) =>
        new(
            TenantId,
            task.Id,
            Owner,
            HasManageAll: true,
            CanAssign: true,
            Title: task.Title.Value,
            Description: null,
            Priority: task.Priority,
            DueOn: null,
            IsStatutory: false,
            StatutoryChangeReason: null,
            AssigneeUserId: task.AssigneeUserId,
            EstimatedMinutes: null,
            CustomerId: task.Reference.CustomerId,
            TaxYear: task.Reference.TaxYear,
            RowVersion: Convert.ToBase64String(Version)
        );

    private static TaskItem NewTask()
    {
        var task = TaskItem
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
                DateTime.UtcNow
            )
            .Value;
        typeof(TaskItem).GetProperty(nameof(TaskItem.RowVersion))!.SetValue(task, Version);
        return task;
    }

    private sealed class Fixture(params TaskItem[] seed)
    {
        public InMemoryTaskRepository Tasks { get; } = new(seed);
        public RecordingUnitOfWork UnitOfWork { get; } = new();
        public FakeMessageBus Bus { get; } = new();

        public Task<BuildingBlocks.Results.Result<TaskResponse>> RunAsync(
            UpdateTaskCommand command,
            bool canAssign = true
        ) =>
            UpdateTaskHandler.Handle(
                command with
                {
                    CanAssign = canAssign,
                },
                Tasks,
                UnitOfWork,
                Bus,
                new NoOpCorrelationContext(),
                new FixedOfficeZone("America/New_York"),
                new EveryCustomer(),
                CancellationToken.None
            );
    }
}
