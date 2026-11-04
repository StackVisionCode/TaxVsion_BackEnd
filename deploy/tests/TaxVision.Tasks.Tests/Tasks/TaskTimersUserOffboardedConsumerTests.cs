using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Timers.Consumers;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Tasks;

// Al retirar a alguien, el sistema cierra sus timers en marcha e imputa el tramo; los de otros siguen.
public sealed class TaskTimersUserOffboardedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Leaver = Guid.NewGuid();
    private static readonly Guid Colleague = Guid.NewGuid();

    [Fact]
    public async Task Running_timers_of_the_leaver_are_stopped_at_the_offboarding_time()
    {
        var removedAt = DateTime.UtcNow.AddMinutes(-5);
        var task = NewTask();
        var mine = task.StartTimer(Leaver, isBillable: true, removedAt.AddHours(-2)).Value;
        var theirs = task.StartTimer(Colleague, isBillable: true, removedAt.AddHours(-1)).Value;
        var uow = new RecordingUnitOfWork();

        await RunAsync(new InMemoryTaskRepository(task), uow, removedAt);

        Assert.False(mine.IsRunning);
        Assert.Equal(removedAt, mine.StoppedAtUtc);
        Assert.Equal(2m, task.ActualHours);
        Assert.True(theirs.IsRunning);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task Without_running_timers_nothing_is_saved()
    {
        var uow = new RecordingUnitOfWork();

        await RunAsync(new InMemoryTaskRepository(NewTask()), uow, DateTime.UtcNow);

        Assert.Equal(0, uow.SaveCount);
    }

    private static Task RunAsync(InMemoryTaskRepository tasks, RecordingUnitOfWork uow, DateTime removedAt) =>
        TaskTimersUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = Leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                RemovedAtUtc = removedAt,
            },
            tasks,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<TaskTimer>.Instance,
            CancellationToken.None
        );

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                Colleague,
                TaskTitle.Create("Prepare return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Leaver,
                DateTime.UtcNow.AddDays(-1)
            )
            .Value;
}
