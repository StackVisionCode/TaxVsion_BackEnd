using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

public sealed class MoveTaskToWaitingOnClientHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>P4: la fecha pedida al cliente vence al terminar ese día en la oficina.</summary>
    [Fact]
    public async Task A_civil_client_due_ends_at_the_end_of_the_office_day()
    {
        var task = TaskItem
            .Create(
                TenantId,
                UserId,
                TaskTitle.Create("Preparar 1040").Value,
                null,
                TaskPriority.Normal,
                TaskReference.Create(Guid.NewGuid(), 2025).Value,
                null,
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
        var bus = new FakeMessageBus();

        var result = await MoveTaskToWaitingOnClientHandler.Handle(
            new MoveTaskToWaitingOnClientCommand(
                TenantId,
                task.Id,
                UserId,
                true,
                "W-2",
                null,
                ClientDueOn: new DateOnly(2026, 11, 15)
            ),
            new InMemoryTaskRepository(task),
            new RecordingUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            new FixedOfficeZone("America/New_York"),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTime(2026, 11, 16, 4, 59, 59, DateTimeKind.Utc), result.Value.ClientDueAtUtc);
        Assert.Equal(new DateOnly(2026, 11, 15), result.Value.ClientDueOn);
        var waiting = Assert.Single(bus.Published.OfType<TaskWaitingOnClientIntegrationEvent>());
        Assert.Equal(new DateOnly(2026, 11, 15), waiting.ClientDueOn);
    }
}
