using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quartz;
using TaxVision.Reminder.Application.Reminders.Commands;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Infrastructure.Scheduling;
using TaxVision.Reminder.Tests.Reminders;

namespace TaxVision.Reminder.Tests.Scheduling;

/// <summary>BB-12: un disparo que choca con otra edición se relee. Si se perdiera, el aviso no sonaría a su hora.</summary>
public sealed class ReminderFireJobTests
{
    private static readonly ConflictException Conflict = new("Persistence.ConcurrencyConflict", "Modified.");

    [Fact]
    public async Task ConcurrencyConflict_RefiresImmediately()
    {
        var failure = await Assert.ThrowsAsync<JobExecutionException>(() => RunAsync(Conflict, refireCount: 0));

        Assert.True(failure.RefireImmediately);
    }

    /// <summary>Con el tope agotado lo retoma la reconciliación: el recordatorio sigue pendiente y sin trigger.</summary>
    [Fact]
    public async Task ConcurrencyConflict_AfterTheLimit_DoesNotRefire()
    {
        var failure = await Assert.ThrowsAsync<ConflictException>(() => RunAsync(Conflict, refireCount: 3));

        Assert.Same(Conflict, failure);
    }

    [Fact]
    public async Task OtherConflict_DoesNotRefire()
    {
        var unique = new ConflictException("Persistence.UniqueConstraint", "Duplicate.");

        await Assert.ThrowsAsync<ConflictException>(() => RunAsync(unique, refireCount: 0));
    }

    /// <summary>
    /// El handler corre en un scope nuevo de Wolverine, con la correlación vacía: el job tiene que traerla en el
    /// comando para que la traza enlace el disparo con sus consumidores.
    /// </summary>
    [Fact]
    public async Task Job_publishes_with_a_correlation_id()
    {
        var reminders = new FakeReminderRepository();
        var reminder = FireReminderHandlerTests.Seed(reminders, DateTime.UtcNow.AddMinutes(-1));
        var jobBus = new RecordingMessageBus { OnInvoke = () => { } };

        await new ReminderFireJob(
            jobBus,
            Options.Create(new ReminderSchedulingOptions()),
            NullLogger<ReminderFireJob>.Instance
        ).Execute(FireContextProxy.Create(reminder.TenantId, reminder.Id, refireCount: 0));

        var handlerBus = new RecordingMessageBus();
        await FireReminderHandler.Handle(
            Assert.IsType<FireReminderCommand>(Assert.Single(jobBus.Invoked)),
            reminders,
            new NoOpUnitOfWork(),
            handlerBus,
            new CorrelationContext(),
            new RecordingReminderMetrics(),
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

        var due = Assert.IsType<ReminderDueIntegrationEvent>(Assert.Single(handlerBus.Published));
        Assert.False(string.IsNullOrWhiteSpace(due.CorrelationId));
    }

    private static Task RunAsync(Exception failure, int refireCount) =>
        new ReminderFireJob(
            new RecordingMessageBus { OnInvoke = () => throw failure },
            Options.Create(new ReminderSchedulingOptions()),
            NullLogger<ReminderFireJob>.Instance
        ).Execute(FireContextProxy.Create(Guid.NewGuid(), Guid.NewGuid(), refireCount));
}
