using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Reminder.Application.Reminders.Consumers;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Tests.Reminders;

/// <summary>
/// Al retirar (offboard) a un empleado se cancelan y desagendan sus recordatorios PRIVADOS (General).
/// Los atados a un target (Task/Calendar/Note) y los de otros usuarios no se tocan aquí.
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
    public async Task Cancels_and_unschedules_the_leavers_private_reminders_only()
    {
        var leaver = Guid.NewGuid();
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();
        var minePrivate = Seed(reminders, leaver, ReminderCategory.General, null);
        var mineTask = Seed(reminders, leaver, ReminderCategory.Task, Guid.NewGuid());
        var otherPrivate = Seed(reminders, Guid.NewGuid(), ReminderCategory.General, null);

        await Run(reminders, scheduler, metrics, leaver);

        Assert.Equal(ReminderStatus.Cancelled, minePrivate.Status);
        Assert.Equal(ReminderCancellationReasons.OwnerOffboarded, minePrivate.CancellationReason);
        Assert.Contains(minePrivate.Id, scheduler.Unscheduled);
        Assert.Equal([ReminderCancellationReasons.OwnerOffboarded], metrics.Cancelled);

        Assert.Equal(ReminderStatus.Scheduled, mineTask.Status); // atado a target: lo cierra su propio servicio
        Assert.Equal(ReminderStatus.Scheduled, otherPrivate.Status); // de otro usuario: intacto
    }

    [Fact]
    public async Task No_private_reminders_is_a_noop()
    {
        var leaver = Guid.NewGuid();
        var reminders = new FakeReminderRepository();
        var scheduler = new RecordingScheduler();
        var metrics = new RecordingReminderMetrics();
        Seed(reminders, leaver, ReminderCategory.Task, Guid.NewGuid());

        await Run(reminders, scheduler, metrics, leaver);

        Assert.Empty(scheduler.Unscheduled);
        Assert.Empty(metrics.Cancelled);
    }
}
