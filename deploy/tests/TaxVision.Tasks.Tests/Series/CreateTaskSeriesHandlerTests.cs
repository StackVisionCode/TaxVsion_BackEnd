using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Commands;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Series;

// La primera ocurrencia de una serie pide su recordatorio como una tarea creada a mano.
public sealed class CreateTaskSeriesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();

    [Fact]
    public async Task FirstInstanceWithDueAndAssignee_RequestsReminder()
    {
        var tasks = new InMemoryTaskRepository();
        var series = new InMemoryTaskSeriesRepository();
        var bus = new FakeMessageBus();

        var result = await CreateTaskSeriesHandler.Handle(
            new CreateTaskSeriesCommand(
                TenantId,
                Manager,
                "Monthly bookkeeping",
                null,
                TaskPriority.Normal,
                null,
                null,
                null,
                Preparer,
                false,
                "FREQ=MONTHLY;INTERVAL=1",
                "America/New_York",
                RecurrenceMode.FixedSchedule,
                DateTime.UtcNow.AddDays(2),
                null,
                null
            ),
            series,
            new TaskSeriesMaterializer(tasks, series),
            new RecordingUnitOfWork(),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, tasks);

        Assert.True(result.IsSuccess);
        var instance = Assert.Single(tasks.Stored);
        var reminder = Assert.Single(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Equal(instance.Id, reminder.TargetId);
        Assert.Equal(TaskReminderContracts.RequestKey(instance.Id, Preparer, 0), reminder.RequestKey);
    }
}
