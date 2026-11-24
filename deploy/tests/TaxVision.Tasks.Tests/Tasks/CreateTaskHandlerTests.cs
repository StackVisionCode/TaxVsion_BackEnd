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

    /// <summary>P4: con solo la fecha civil, el vencimiento es el último segundo de ese día en la oficina.</summary>
    [Fact]
    public async Task CreateWithCivilDueDate_EndsAtTheEndOfTheOfficeDay()
    {
        var bus = new FakeMessageBus();
        var tasks = new InMemoryTaskRepository();

        var result = await CreateTaskHandler.Handle(
            new CreateTaskCommand(
                TenantId,
                Creator,
                "Prepare return",
                null,
                TaskPriority.Normal,
                null,
                null,
                null,
                DueAtUtc: null,
                DueTimeZoneId: null,
                DueIsStatutory: false,
                EstimatedHours: null,
                DueOn: new DateOnly(2026, 11, 15)
            ),
            tasks,
            new RecordingUnitOfWork(),
            new RecordingTaskMetrics(),
            new FixedOfficeZone("America/New_York"),
            new EveryCustomer(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, tasks);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTime(2026, 11, 16, 4, 59, 59, DateTimeKind.Utc), result.Value.DueAtUtc);
        Assert.Equal(new DateOnly(2026, 11, 15), result.Value.DueOn);
        Assert.Equal("America/New_York", result.Value.DueTimeZoneId);
        var created = Assert.Single(bus.Published.OfType<TaskCreatedIntegrationEvent>());
        Assert.Equal(new DateOnly(2026, 11, 15), created.DueOn);
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
            new FixedOfficeZone("America/New_York"),
            new EveryCustomer(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, tasks);
        return result;
    }
}
