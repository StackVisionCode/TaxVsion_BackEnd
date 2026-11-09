using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Application.Generations.FileStored;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

public sealed class DocumentFileScanRejectedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string CopyOwnerType = "Signature";
    private const string CopyFolderType = "Signatures";

    [Fact]
    public async Task Handle_FileScanFailedForUploadingGeneration_FailsAndPublishes()
    {
        var fileId = Guid.NewGuid();
        var externalRef = Guid.NewGuid();
        var generation = UploadingGeneration(fileId, externalRef);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(ScanFailed(fileId, "blob_missing"), new InMemoryRepository(generation), unitOfWork, bus);

        Assert.Equal(DocumentGenerationStatus.Failed, generation.Status);
        Assert.Equal("Documents.Storage.ScanRejected", generation.ErrorCode);
        Assert.Contains("blob_missing", generation.ErrorMessage);
        Assert.Equal(1, unitOfWork.SaveCount);

        var failed = Assert.IsType<DocumentGenerationFailedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(generation.Id, failed.GenerationId);
        Assert.Equal(externalRef, failed.ExternalReferenceId);
        Assert.Equal(TenantId, failed.TenantId);
        Assert.Equal("Documents.Storage.ScanRejected", failed.ErrorCode);
    }

    [Fact]
    public async Task Handle_FileInfectedForUploadingGeneration_FailsAndPublishes()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        var bus = new RecordingMessageBus();

        await DocumentFileScanRejectedConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "corr-1",
                FileId = fileId,
                ObjectKey = "k",
                ScanReport = "Eicar-Test-Signature",
            },
            new InMemoryRepository(generation),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext("corr-1"),
            TimeProvider.System,
            NullLogger<FileInfectedDetectedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(DocumentGenerationStatus.Failed, generation.Status);
        Assert.Single(bus.Published.OfType<DocumentGenerationFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_FileBlockedByPolicyForUploadingGeneration_FailsAndPublishes()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        var bus = new RecordingMessageBus();

        await DocumentFileScanRejectedConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "corr-1",
                FileId = fileId,
                ObjectKey = "k",
                PolicyReason = "policy",
                CreatedBy = Guid.NewGuid(),
            },
            new InMemoryRepository(generation),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext("corr-1"),
            TimeProvider.System,
            NullLogger<FileBlockedByPolicyIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(DocumentGenerationStatus.Failed, generation.Status);
        Assert.Single(bus.Published.OfType<DocumentGenerationFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_TenantMismatch_DoesNothing()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(
            ScanFailed(fileId) with
            {
                TenantId = Guid.NewGuid(),
            },
            new InMemoryRepository(generation),
            unitOfWork,
            bus
        );

        Assert.Equal(DocumentGenerationStatus.Uploading, generation.Status);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_FileOfAnotherService_DoesNothing()
    {
        var generation = UploadingGeneration(Guid.NewGuid());
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(ScanFailed(Guid.NewGuid()), new InMemoryRepository(generation), unitOfWork, bus);

        Assert.Equal(DocumentGenerationStatus.Uploading, generation.Status);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_SignatureCopyInfected_DetachesCopy()
    {
        var copyFileId = Guid.NewGuid();
        var generation = UploadingGeneration(Guid.NewGuid());
        generation.AttachSignatureCopy(copyFileId, 2, CopyOwnerType, CopyFolderType);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await DocumentFileScanRejectedConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "corr-1",
                FileId = copyFileId,
                ObjectKey = "k",
                ScanReport = "Eicar-Test-Signature",
            },
            new InMemoryRepository(generation),
            unitOfWork,
            bus,
            new TestCorrelationContext("corr-1"),
            TimeProvider.System,
            NullLogger<FileInfectedDetectedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(DocumentGenerationStatus.Uploading, generation.Status);
        Assert.Null(generation.SignatureCopyFileId);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_MainRejectedWithStoredCopy_RequestsCopyDeletion()
    {
        var fileId = Guid.NewGuid();
        var copyFileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        generation.AttachSignatureCopy(copyFileId, 2, CopyOwnerType, CopyFolderType);
        var bus = new RecordingMessageBus();

        await HandleAsync(ScanFailed(fileId), new InMemoryRepository(generation), new RecordingUnitOfWork(), bus);

        Assert.Equal(DocumentGenerationStatus.Failed, generation.Status);
        Assert.Null(generation.SignatureCopyFileId);
        var deletion = Assert.Single(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
        Assert.Equal(copyFileId, Assert.Single(deletion.Files).FileId);
    }

    [Fact]
    public async Task Handle_GenerationAlreadyCompleted_DoesNothing()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        var now = DateTime.UtcNow;
        Assert.True(generation.MarkStored(new StorageReference(fileId, "application/pdf", 10, null), now).IsSuccess);
        Assert.True(generation.Complete(now).IsSuccess);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(ScanFailed(fileId), new InMemoryRepository(generation), unitOfWork, bus);

        Assert.Equal(DocumentGenerationStatus.Completed, generation.Status);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_Redelivery_PublishesOnce()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        var repository = new InMemoryRepository(generation);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(ScanFailed(fileId), repository, unitOfWork, bus);
        await HandleAsync(ScanFailed(fileId), repository, unitOfWork, bus);

        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Single(bus.Published);
    }

    private static Task HandleAsync(
        FileScanFailedIntegrationEvent evt,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus
    ) =>
        DocumentFileScanRejectedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            bus,
            new TestCorrelationContext("corr-1"),
            TimeProvider.System,
            NullLogger<FileScanFailedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private static FileScanFailedIntegrationEvent ScanFailed(Guid fileId, string reason = "scan_stalled") =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-1",
            FileId = fileId,
            ObjectKey = "k",
            Reason = reason,
            OwnerType = "Customer",
            FolderType = "ClientForms",
        };

    private static DocumentGeneration UploadingGeneration(Guid fileId, Guid? externalReferenceId = null)
    {
        var now = DateTime.UtcNow;
        var generation = DocumentGeneration
            .Request(
                tenantId: TenantId,
                documentType: DocumentType.Create("client_form.submission_summary").Value,
                templateKey: TemplateKey.Create("client_form.submission_summary").Value,
                templateVersion: 1,
                outputFormat: DocumentOutputFormat.Pdf,
                owner: new GenerationOwner("Customer", Guid.NewGuid()),
                sourceService: "tasks",
                documentVersion: 1,
                priority: DocumentPriority.Normal,
                idempotencyKey: $"key-{Guid.NewGuid():N}",
                correlationId: "corr-1",
                causationId: null,
                nowUtc: now,
                externalReferenceId: externalReferenceId
            )
            .Value;
        Assert.True(generation.Queue(now).IsSuccess);
        Assert.True(generation.StartRendering(now).IsSuccess);
        Assert.True(generation.StartUploading(fileId, now).IsSuccess);
        return generation;
    }

    private sealed class InMemoryRepository(params DocumentGeneration[] generations) : IDocumentGenerationRepository
    {
        private readonly List<DocumentGeneration> _generations = [.. generations];

        public Task<DocumentGeneration?> GetByIdAsync(
            Guid tenantId,
            Guid generationId,
            CancellationToken ct = default
        ) => Task.FromResult(_generations.FirstOrDefault(g => g.TenantId == tenantId && g.Id == generationId));

        public Task<DocumentGeneration?> GetByIdempotencyKeyAsync(
            Guid tenantId,
            string sourceService,
            string idempotencyKey,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _generations.FirstOrDefault(g =>
                    g.TenantId == tenantId && g.SourceService == sourceService && g.IdempotencyKey == idempotencyKey
                )
            );

        public Task<DocumentGeneration?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(_generations.FirstOrDefault(g => g.FileId == fileId));

        public Task<DocumentGeneration?> GetBySignatureCopyFileIdAsync(Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(_generations.FirstOrDefault(g => g.SignatureCopyFileId == fileId));

        public Task AddAsync(DocumentGeneration generation, CancellationToken ct = default)
        {
            _generations.Add(generation);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class TestCorrelationContext(string correlationId) : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = correlationId;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            var previous = CorrelationId;
            CorrelationId = correlationId;
            return new Popper(() => CorrelationId = previous);
        }

        private sealed class Popper(Action pop) : IDisposable
        {
            public void Dispose() => pop();
        }
    }

    private sealed class RecordingMessageBus : IMessageBus
    {
        public List<object> Published { get; } = [];

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            if (message is not null)
                Published.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public string? TenantId { get; set; }

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();
    }
}
