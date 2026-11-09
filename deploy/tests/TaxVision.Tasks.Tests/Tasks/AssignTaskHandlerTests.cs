using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

// El recordatorio de vencimiento es por persona: al cambiar de asignado, el nuevo pide el suyo.
public sealed class AssignTaskHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();

    [Fact]
    public async Task Reassigning_a_task_with_a_due_date_requests_the_reminder_for_the_new_assignee()
    {
        var newAssignee = Guid.NewGuid();
        var task = NewTask(DueDate.Create(DateTime.UtcNow.AddDays(2), "America/New_York", false).Value);
        var bus = new FakeMessageBus();

        var result = await AssignAsync(task, newAssignee, bus);

        Assert.True(result.IsSuccess);
        Assert.Single(bus.Published.OfType<TaskAssignedIntegrationEvent>());
        var reminder = Assert.Single(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Equal(newAssignee, reminder.UserId);
        Assert.Equal(task.Id, reminder.TargetId);
    }

    [Fact]
    public async Task Assigning_to_the_same_person_requests_nothing()
    {
        var task = NewTask(DueDate.Create(DateTime.UtcNow.AddDays(2), "America/New_York", false).Value);
        var bus = new FakeMessageBus();

        await AssignAsync(task, Owner, bus);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Without_a_due_date_no_reminder_is_requested()
    {
        var task = NewTask(null);
        var bus = new FakeMessageBus();

        await AssignAsync(task, Guid.NewGuid(), bus);

        Assert.Empty(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
    }

    // El aviso y el recordatorio salen del handler del evento de dominio (el replay los deja en el bus).
    private static async Task<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>> AssignAsync(
        TaskItem task,
        Guid assignee,
        FakeMessageBus bus
    )
    {
        var tasks = new InMemoryTaskRepository(task);
        var result = await AssignTaskHandler.Handle(
            new AssignTaskCommand(TenantId, task.Id, assignee, Owner, HasManageAll: false),
            tasks,
            new RecordingUnitOfWork(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, tasks);
        return result;
    }

    private static TaskItem NewTask(DueDate? due) =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create("File the return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                due,
                null,
                Owner,
                DateTime.UtcNow
            )
            .Value;
}
