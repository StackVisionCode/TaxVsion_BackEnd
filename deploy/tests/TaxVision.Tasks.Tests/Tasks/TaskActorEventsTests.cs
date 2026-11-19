using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Application.Attachments.Commands;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>Work Profile: los eventos de Tasks llevan quién actuó (campos opcionales del contrato).</summary>
public sealed class TaskActorEventsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public void TaskCreated_carries_the_creator()
    {
        var task = NewTask();
        var born = new TaskCreatedDomainEvent(task.Id, TenantId, Actor, null, null, DateTime.UtcNow);

        var created = TaskBirthAnnouncements.For(task, born, "test").OfType<TaskCreatedIntegrationEvent>().Single();

        Assert.Equal(Actor, created.CreatedByUserId);
    }

    [Fact]
    public void TaskCreated_of_a_series_occurrence_has_no_actor()
    {
        var task = NewTask();
        var born = new TaskCreatedDomainEvent(task.Id, TenantId, Actor, null, null, DateTime.UtcNow, true);

        var created = TaskBirthAnnouncements.For(task, born, "test").OfType<TaskCreatedIntegrationEvent>().Single();

        Assert.Null(created.CreatedByUserId);
    }

    [Fact]
    public async Task TaskDueChanged_carries_who_changed_it()
    {
        var task = NewTask();
        var bus = new FakeMessageBus();

        var result = await ChangeTaskDueHandler.Handle(
            new ChangeTaskDueCommand(
                TenantId,
                task.Id,
                Actor,
                false,
                DateTime.UtcNow.AddDays(9),
                "America/New_York",
                false,
                null
            ),
            new InMemoryTaskRepository(task),
            new RecordingUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            new FixedOfficeZone("America/New_York"),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(Actor, Assert.Single(bus.Published.OfType<TaskDueChangedIntegrationEvent>()).ChangedByUserId);
    }

    [Fact]
    public async Task TaskAttachmentDetached_carries_who_detached_it()
    {
        var task = NewTask();
        var fileId = Guid.NewGuid();
        Assert.True(task.LinkExistingFile(fileId, "W-2.pdf", "application/pdf", 10, Actor, DateTime.UtcNow).IsSuccess);
        var bus = new FakeMessageBus();

        var result = await DetachTaskAttachmentHandler.Handle(
            new DetachTaskAttachmentCommand(TenantId, task.Id, fileId, Actor),
            new InMemoryTaskRepository(task),
            new RecordingUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Actor,
            Assert.Single(bus.Published.OfType<TaskAttachmentDetachedIntegrationEvent>()).DetachedByUserId
        );
    }

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                Actor,
                TaskTitle.Create("Prepare return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                Actor,
                DateTime.UtcNow
            )
            .Value;
}
