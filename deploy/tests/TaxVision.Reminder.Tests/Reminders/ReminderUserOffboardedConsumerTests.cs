using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Reminder.Application.Reminders.Consumers;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Tests.Reminders;

/// <summary>
/// Al retirar (offboard) a un empleado se cancelan y desagendan TODOS sus recordatorios pendientes
/// (General, Task, Calendar y Note). Los de otros usuarios y los ya cerrados no se tocan.
/// </summary>
public sealed class ReminderUserOffboardedConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");

    private static int _keySeq;

    private static ReminderAggregate Seed(
        FakeReminderRepository reminders,
        Guid userId,
        ReminderCategory category,
        Guid? targetId
    )
    {
        var nowUtc = DateTime.UtcNow;
        var reminder = ReminderAggregate
            .Create(
                TenantId,
                userId,
                ReminderSubject.Create("Llamar a Pérez", body: null).Value,
                ReminderTarget.Create(category, targetId).Value,
                ReminderSchedule.Absolute(nowUtc.AddHours(2), nowUtc).Value,
                ReminderTimeZone.Utc,
                RequestKey.Create($"test:offboard:{Interlocked.Increment(ref _keySeq)}").Value,
                nowUtc
            )
            .Value;
        reminders.Seed(reminder);
        return reminder;
    }

    private static Task Run(
        FakeReminderRepository reminders,
        RecordingScheduler scheduler,
        RecordingReminderMetrics metrics,
        Guid leaver
    ) =>
        ReminderUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                RemovedAtUtc = DateTime.UtcNow,
            },
            reminders,
            scheduler,
            new NoOpUnitOfWork(),
            new FixedCorrelationContext(),
            metrics,
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Cancels_and_unschedules_every_pending_reminder_of_the_leaver()
    {
        var leaver = Guid.NewGuid();
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();
        var mine = new[]
        {
            Seed(reminders, leaver, ReminderCategory.General, null),
            Seed(reminders, leaver, ReminderCategory.Task, Guid.NewGuid()),
            Seed(reminders, leaver, ReminderCategory.Calendar, Guid.NewGuid()),
            Seed(reminders, leaver, ReminderCategory.Note, Guid.NewGuid()),
        };
        var otherUsers = Seed(reminders, Guid.NewGuid(), ReminderCategory.Task, Guid.NewGuid());

        await Run(reminders, scheduler, metrics, leaver);

        Assert.All(
            mine,
            reminder =>
            {
                Assert.Equal(ReminderStatus.Cancelled, reminder.Status);
                Assert.Equal(ReminderCancellationReasons.OwnerOffboarded, reminder.CancellationReason);
                Assert.Contains(reminder.Id, scheduler.Unscheduled);
            }
        );
        Assert.Equal(4, metrics.Cancelled.Count);
        Assert.Equal(ReminderStatus.Scheduled, otherUsers.Status);
    }

    [Fact]
    public async Task Redelivery_is_a_noop_once_cancelled()
    {
        var leaver = Guid.NewGuid();
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();
        Seed(reminders, leaver, ReminderCategory.Calendar, Guid.NewGuid());

        await Run(reminders, scheduler, metrics, leaver);
        await Run(reminders, scheduler, metrics, leaver);

        Assert.Single(scheduler.Unscheduled);
        Assert.Single(metrics.Cancelled);
    }

    [Fact]
    public async Task No_pending_reminders_is_a_noop()
    {
        var leaver = Guid.NewGuid();
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();
        Seed(reminders, Guid.NewGuid(), ReminderCategory.Task, Guid.NewGuid());

        await Run(reminders, scheduler, metrics, leaver);

        Assert.Empty(scheduler.Unscheduled);
        Assert.Empty(metrics.Cancelled);
    }
}
