using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

// Notification solo escucha TaskAssigned: crear una tarea ya asignada tiene que emitirlo.
public sealed class CreateTaskHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Assignee = Guid.NewGuid();

    [Fact]
    public async Task CreateWithAssigneeOtherThanCreator_PublishesTaskAssigned()
    {
        var bus = new FakeMessageBus();

        var result = await CreateAsync(Assignee, bus);

        Assert.True(result.IsSuccess);
        var assigned = Assert.Single(bus.Published.OfType<TaskAssignedIntegrationEvent>());
        Assert.Equal(result.Value.Id, assigned.TaskId);
        Assert.Equal(Assignee, assigned.AssigneeUserId);
        Assert.Null(assigned.PreviousAssigneeUserId);
        Assert.Equal(Creator, assigned.AssignedByUserId);
        Assert.Single(bus.Published.OfType<TaskCreatedIntegrationEvent>());
    }

    [Fact]
    public async Task CreateWithoutAssignee_DoesNotPublishTaskAssigned()
    {
        var bus = new FakeMessageBus();

        var result = await CreateAsync(null, bus);

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Published.OfType<TaskAssignedIntegrationEvent>());
    }

    // El aviso sale del handler del evento de dominio de alta (DomainEventReplay lo deja en el bus).
    private static async Task<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>> CreateAsync(
        Guid? assignee,
        FakeMessageBus bus
    )
    {
        var tasks = new InMemoryTaskRepository();
        var result = await CreateTaskHandler.Handle(
            new CreateTaskCommand(
                TenantId,
                Creator,
                "Prepare return",
                null,
                TaskPriority.Normal,
                assignee,
                null,
                null,
                DateTime.UtcNow.AddDays(3),
                "America/New_York",
                false,
                null
            ),
            tasks,
            new RecordingUnitOfWork(),
            new RecordingTaskMetrics(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, tasks);
        return result;
    }
}
