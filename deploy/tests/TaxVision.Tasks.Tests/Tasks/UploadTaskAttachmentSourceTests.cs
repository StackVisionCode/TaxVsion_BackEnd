using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Attachments;
using TaxVision.Tasks.Application.Attachments.Commands;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>Subir un adjunto declara el archivo a CloudStorage después de guardar; si el alta falla, no sale nada.</summary>
public sealed class UploadTaskAttachmentSourceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    [Fact]
    public async Task Uploading_publishes_the_declaration_after_saving()
    {
        var task = NewTask();
        var bus = new FakeMessageBus();
        var unitOfWork = new SnapshotUnitOfWork(bus);
        var fileId = Guid.NewGuid();

        var result = await HandleAsync(task, fileId, bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.PublishedAtSave);
        Assert.IsType<TaskAttachmentAddedIntegrationEvent>(bus.Published[0]);
        var declared = Assert.Single(bus.Published.OfType<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(
            (fileId, "task", task.Id, "attachment", (Guid?)CustomerId),
            (
                declared.FileId,
                declared.SourceResourceType,
                declared.SourceResourceId,
                declared.SourceRole,
                declared.RelatedCustomerId
            )
        );
    }

    [Fact]
    public async Task A_failed_attach_publishes_nothing()
    {
        var task = NewTask();
        var fileId = Guid.NewGuid();
        Assert.True(
            task.AttachUploadedFile(fileId, "W-2.pdf", "application/pdf", 10, Owner, DateTime.UtcNow).IsSuccess
        );
        var bus = new FakeMessageBus();
        var unitOfWork = new SnapshotUnitOfWork(bus);

        var result = await HandleAsync(task, fileId, bus, unitOfWork);

        Assert.True(result.IsFailure);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.Saves);
    }

    private static Task<Result<TaskAttachmentResponse>> HandleAsync(
        TaskItem task,
        Guid fileId,
        FakeMessageBus bus,
        IUnitOfWork unitOfWork
    ) =>
        UploadTaskAttachmentHandler.Handle(
            new UploadTaskAttachmentCommand(TenantId, Owner, task.Id, fileId, "W-2.pdf", "application/pdf", 10),
            new InMemoryTaskRepository(task),
            unitOfWork,
            bus,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                TenantId,
                Owner,
                TaskTitle.Create("Prepare return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.Create(CustomerId, 2025).Value,
                null,
                null,
                Owner,
                DateTime.UtcNow
            )
            .Value;

    // Anota cuántos mensajes había publicados en el momento de guardar.
    private sealed class SnapshotUnitOfWork(FakeMessageBus bus) : IUnitOfWork
    {
        public int Saves { get; private set; }
        public int PublishedAtSave { get; private set; } = -1;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            PublishedAtSave = bus.Published.Count;
            return Task.FromResult(0);
        }
    }
}
