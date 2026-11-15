using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.TimeZones;
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
            new FixedOfficeZone("America/New_York"),
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(bus, tasks);

        Assert.True(result.IsSuccess);
        var instance = Assert.Single(tasks.Stored);
        var reminder = Assert.Single(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Equal(instance.Id, reminder.TargetId);
        Assert.Equal(TaskReminderContracts.RequestKey(instance.Id, Preparer, 0), reminder.RequestKey);
    }

    /// <summary>P4/P24: con la fecha civil, la serie vence al final de ese día y cuenta en la zona de la oficina.</summary>
    [Fact]
    public async Task A_civil_anchor_ends_at_the_end_of_the_office_day_in_the_office_zone()
    {
        var tasks = new InMemoryTaskRepository();
        var series = new InMemoryTaskSeriesRepository();

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
                "Etc/UTC",
                RecurrenceMode.FixedSchedule,
                default,
                null,
                null,
                AnchorOn: new DateOnly(2027, 1, 15)
            ),
            series,
            new TaskSeriesMaterializer(tasks, series),
            new RecordingUnitOfWork(),
            new FixedOfficeZone("America/New_York"),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var instance = Assert.Single(tasks.Stored);
        Assert.Equal(
            LocalDay.EndUtc(new DateOnly(2027, 1, 15), ResolvedTimeZone.Resolve("America/New_York")),
            instance.Due!.DueAtUtc
        );
        Assert.Equal("America/New_York", instance.Due.TimeZoneId);
    }
}
