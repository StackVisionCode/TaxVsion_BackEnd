using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Commands;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Series;

// Reanudar una serie sin instancia abierta materializa una y le pide su recordatorio.
public sealed class ResumeTaskSeriesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();

    [Fact]
    public async Task MaterializedInstance_RequestsReminder()
    {
        var paused = NewSeries();
        paused.Pause();
        var tasks = new InMemoryTaskRepository();
        var series = new InMemoryTaskSeriesRepository(paused);
        var bus = new FakeMessageBus();

        var result = await ResumeTaskSeriesHandler.Handle(
            new ResumeTaskSeriesCommand(TenantId, paused.Id, Manager, HasManageAll: true),
            series,
            new TaskSeriesMaterializer(tasks, series),
            new RecordingUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var instance = Assert.Single(tasks.Stored);
        var reminder = Assert.Single(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Equal(instance.Id, reminder.TargetId);
        Assert.Equal(Preparer, reminder.UserId);
    }

    internal static TaskSeries NewSeries(Guid? tenantId = null) =>
        TaskSeries
            .Create(
                tenantId ?? TenantId,
                Manager,
                RecurrenceRule.Create("FREQ=MONTHLY;INTERVAL=1", "America/New_York").Value,
                RecurrenceMode.FixedSchedule,
                new TaskItemBlueprint
                {
                    Title = TaskTitle.Create("Monthly bookkeeping").Value,
                    Priority = TaskPriority.Normal,
                    Reference = TaskReference.None,
                    AssigneeUserId = Preparer,
                    IsStatutory = false,
                },
                DateTime.UtcNow.AddDays(2),
                null,
                null,
                DateTime.UtcNow
            )
            .Value;
}
