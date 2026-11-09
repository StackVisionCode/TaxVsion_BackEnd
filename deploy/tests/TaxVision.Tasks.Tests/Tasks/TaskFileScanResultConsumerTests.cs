using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Attachments.Consumers;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Tests.Dependencies;

namespace TaxVision.Tasks.Tests.Tasks;

/// <summary>BB-16: el aviso de un adjunto rechazado o borrado sale después de guardar, no antes.</summary>
public sealed class TaskFileScanResultConsumerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_infected_file_is_rejected_and_announced_after_saving()
    {
        var world = new World();

        await TaskFileScanResultConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                ObjectKey = "k",
                ScanReport = "eicar",
                TenantId = world.Task.TenantId,
                FileId = world.FileId,
                CorrelationId = "corr",
            },
            world.Tasks,
            world.UnitOfWork,
            world.Bus,
            new NoOpCorrelationContext(),
            NullLogger<TaskItem>.Instance,
            CancellationToken.None
        );

        Assert.Equal(AttachmentStatus.Rejected, world.Task.Attachments.Single().Status);
        world.AssertAnnouncedOnceAfterSaving();
    }

    [Fact]
    public async Task A_blocked_file_is_rejected_and_announced_after_saving()
    {
        var world = new World();

        await TaskFileScanResultConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                ObjectKey = "k",
                PolicyReason = "policy",
                CreatedBy = Guid.NewGuid(),
                TenantId = world.Task.TenantId,
                FileId = world.FileId,
                CorrelationId = "corr",
            },
            world.Tasks,
            world.UnitOfWork,
            world.Bus,
            new NoOpCorrelationContext(),
            NullLogger<TaskItem>.Instance,
            CancellationToken.None
        );

        Assert.Equal(AttachmentStatus.Rejected, world.Task.Attachments.Single().Status);
        world.AssertAnnouncedOnceAfterSaving();
    }

    [Fact]
    public async Task A_file_deleted_at_the_source_is_detached_and_announced_after_saving()
    {
        var world = new World();

        await TaskFileScanResultConsumer.Handle(
            new FileDeletedIntegrationEvent
            {
                CreatedBy = Guid.NewGuid(),
                TenantId = world.Task.TenantId,
                FileId = world.FileId,
                CorrelationId = "corr",
            },
            world.Tasks,
            world.UnitOfWork,
            world.Bus,
            new NoOpCorrelationContext(),
            NullLogger<TaskItem>.Instance,
            CancellationToken.None
        );

        Assert.Equal(AttachmentStatus.Detached, world.Task.Attachments.Single().Status);
        world.AssertAnnouncedOnceAfterSaving();
    }

    [Fact]
    public async Task A_file_of_another_tenant_changes_nothing()
    {
        var world = new World();

        await TaskFileScanResultConsumer.Handle(
            new FileDeletedIntegrationEvent
            {
                CreatedBy = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                FileId = world.FileId,
                CorrelationId = "corr",
            },
            world.Tasks,
            world.UnitOfWork,
            world.Bus,
            new NoOpCorrelationContext(),
            NullLogger<TaskItem>.Instance,
            CancellationToken.None
        );

        Assert.Equal(AttachmentStatus.Pending, world.Task.Attachments.Single().Status);
        Assert.Empty(world.Bus.Published);
        Assert.Equal(0, world.UnitOfWork.Saves);
    }

    private sealed class World
    {
        public World()
        {
            Task = TaskItem
                .Create(
                    Guid.NewGuid(),
                    UserId,
                    TaskTitle.Create("Revisar documentos").Value,
                    null,
                    TaskPriority.Normal,
                    TaskReference.None,
                    null,
                    null,
                    UserId,
                    Now
                )
                .Value;
            Task.AttachUploadedFile(FileId, "1099.pdf", "application/pdf", 512, UserId, Now);
            Tasks = new InMemoryTaskRepository(Task);
            UnitOfWork = new ProbeUnitOfWork(Bus);
        }

        public Guid FileId { get; } = Guid.NewGuid();
        public TaskItem Task { get; }
        public InMemoryTaskRepository Tasks { get; }
        public FakeMessageBus Bus { get; } = new();
        public ProbeUnitOfWork UnitOfWork { get; }

        public void AssertAnnouncedOnceAfterSaving()
        {
            Assert.Equal(1, UnitOfWork.Saves);
            Assert.Equal(0, UnitOfWork.PublishedAtSave);
            Assert.Single(Bus.Published);
        }
    }

    private sealed class ProbeUnitOfWork(FakeMessageBus bus) : IUnitOfWork
    {
        public int Saves { get; private set; }
        public int PublishedAtSave { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            PublishedAtSave = bus.Published.Count;
            return System.Threading.Tasks.Task.FromResult(1);
        }
    }
}
