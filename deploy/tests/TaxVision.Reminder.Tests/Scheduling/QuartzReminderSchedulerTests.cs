using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.ValueObjects;
using TaxVision.Reminder.Infrastructure.Scheduling;
using TaxVision.Reminder.Tests.Reminders;

namespace TaxVision.Reminder.Tests.Scheduling;

/// <summary>REM-3: dos réplicas pueden crear el mismo trigger, y un fallo no corta el barrido.</summary>
public sealed class QuartzReminderSchedulerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime FireAtUtc = DateTime.UtcNow.AddMinutes(30);

    [Fact]
    public async Task Reschedule_TriggerCreatedConcurrently_DoesNotThrow()
    {
        var scheduler = RacingSchedulerProxy.Create(out var state);

        await NewAdapter(scheduler).RescheduleAsync(TenantId, Guid.NewGuid(), FireAtUtc);

        Assert.Equal(1, state.ScheduleCalls);
    }

    [Fact]
    public async Task Schedule_TriggerCreatedConcurrently_DoesNotThrow()
    {
        var scheduler = RacingSchedulerProxy.Create(out var state);

        await NewAdapter(scheduler).ScheduleAsync(TenantId, Guid.NewGuid(), FireAtUtc);

        Assert.Equal(1, state.ScheduleCalls);
    }

    [Fact]
    public async Task ReconciliationJob_OneReminderFails_OthersStillRepaired()
    {
        var repository = new FakeReminderRepository();
        var reminders = new[] { Build("1"), Build("2"), Build("3") };
        foreach (var reminder in reminders)
            repository.Seed(reminder);
        var scheduler = new FailingForOneScheduler(reminders[1].Id);
        using var provider = new ServiceCollection()
            .AddSingleton<IReminderRepository>(repository)
            .BuildServiceProvider();
        var job = new ReminderScheduleReconciliationJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            scheduler,
            Options.Create(new ReminderSchedulingOptions()),
            NullLogger<ReminderScheduleReconciliationJob>.Instance
        );

        await job.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (scheduler.Scheduled.Count < 2 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await job.StopAsync(CancellationToken.None);

        Assert.Contains(reminders[0].Id, scheduler.Scheduled);
        Assert.Contains(reminders[2].Id, scheduler.Scheduled);
    }

    private static QuartzReminderScheduler NewAdapter(Quartz.IScheduler scheduler) =>
        new(new FixedSchedulerFactory(scheduler), NullLogger<QuartzReminderScheduler>.Instance);

    private static ReminderAggregate Build(string key) =>
        ReminderAggregate
            .Create(
                TenantId,
                Guid.NewGuid(),
                ReminderSubject.Create("Call the client", body: null).Value,
                ReminderTarget.Create(ReminderCategory.General, targetId: null).Value,
                ReminderSchedule.Absolute(FireAtUtc, FireAtUtc.AddDays(-1)).Value,
                ReminderTimeZone.Create("UTC").Value,
                RequestKey.Create($"test:sweep:{key}").Value,
                FireAtUtc.AddDays(-1)
            )
            .Value;

    private sealed class FailingForOneScheduler(Guid failFor) : IReminderScheduler
    {
        public List<Guid> Scheduled { get; } = [];

        public Task ScheduleAsync(Guid tenantId, Guid reminderId, DateTime fireAtUtc, CancellationToken ct = default)
        {
            if (reminderId == failFor)
                throw new InvalidOperationException("Quartz is down for this one.");
            Scheduled.Add(reminderId);
            return Task.CompletedTask;
        }

        public Task RescheduleAsync(
            Guid tenantId,
            Guid reminderId,
            DateTime newFireAtUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task UnscheduleAsync(Guid tenantId, Guid reminderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> IsScheduledAsync(Guid tenantId, Guid reminderId, CancellationToken ct = default) =>
            Task.FromResult(false);
    }
}
