using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>Una tarea solo se liga a un cliente que quien la guarda puede ver (S-05).</summary>
public sealed class TaskCustomerAccessTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Mine = Guid.NewGuid();
    private static readonly Guid NotMine = Guid.NewGuid();

    [Fact]
    public async Task A_new_task_can_only_take_a_client_I_can_see()
    {
        var hidden = await CreateAsync(NotMine);
        var visible = await CreateAsync(Mine);
        var none = await CreateAsync(null);

        Assert.Equal(TaskErrors.Reference.CustomerNotVisible, hidden.Error);
        Assert.True(visible.IsSuccess);
        Assert.True(none.IsSuccess);
    }

    [Fact]
    public async Task Changing_to_a_client_I_cannot_see_is_rejected_but_keeping_the_one_it_has_is_not()
    {
        var task = NewTask(NotMine);
        var tasks = new InMemoryTaskRepository(task);

        var other = await UpdateAsync(tasks, task, Guid.NewGuid());
        var same = await UpdateAsync(tasks, task, NotMine);

        Assert.Equal(TaskErrors.Reference.CustomerNotVisible, other.Error);
        Assert.True(same.IsSuccess);
    }

    private static Task<BuildingBlocks.Results.Result<TaskResponse>> CreateAsync(Guid? customer) =>
        CreateTaskHandler.Handle(
            new CreateTaskCommand(
                TenantId,
                Me,
                "Prepare return",
                null,
                TaskPriority.Normal,
                Me,
                customer,
                null,
                null,
                null,
                false,
                null,
                CanViewAllCustomers: false
            ),
            new InMemoryTaskRepository(),
            new RecordingUnitOfWork(),
            new RecordingTaskMetrics(),
            new FixedOfficeZone("America/New_York"),
            new OnlyTheseCustomers(Mine),
            CancellationToken.None
        );

    private static Task<BuildingBlocks.Results.Result<TaskResponse>> UpdateAsync(
        InMemoryTaskRepository tasks,
        TaskItem task,
        Guid customer
    ) =>
        UpdateTaskHandler.Handle(
            new UpdateTaskCommand(
                TenantId,
                task.Id,
                Me,
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
                CustomerId: customer,
                TaxYear: null,
                RowVersion: null,
                CanViewAllCustomers: false
            ),
            tasks,
            new RecordingUnitOfWork(),
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            new FixedOfficeZone("America/New_York"),
            new OnlyTheseCustomers(Mine),
            CancellationToken.None
        );

    private static TaskItem NewTask(Guid customer) =>
        TaskItem
            .Create(
                TenantId,
                Me,
                TaskTitle.Create("Prepare 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.Create(customer, null).Value,
                null,
                null,
                Me,
                DateTime.UtcNow
            )
            .Value;
}
