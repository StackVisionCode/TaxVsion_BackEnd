using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quartz;
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

    private static Task RunAsync(Exception failure, int refireCount) =>
        new ReminderFireJob(
            new RecordingMessageBus { OnInvoke = () => throw failure },
            Options.Create(new ReminderSchedulingOptions()),
            NullLogger<ReminderFireJob>.Instance
        ).Execute(FireContextProxy.Create(Guid.NewGuid(), Guid.NewGuid(), refireCount));
}
