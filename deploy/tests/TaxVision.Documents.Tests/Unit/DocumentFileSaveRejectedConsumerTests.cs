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

public sealed class DocumentFileSaveRejectedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string CopyOwnerType = "Signature";
    private const string CopyFolderType = "Signatures";

    [Fact]
    public async Task Own_rejection_fails_the_uploading_generation_and_announces_it()
    {
        var fileId = Guid.NewGuid();
        var externalRef = Guid.NewGuid();
        var generation = UploadingGeneration(fileId, externalRef);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(fileId), new InMemoryRepository(generation), unitOfWork, bus);

        Assert.Equal(DocumentGenerationStatus.Failed, generation.Status);
        Assert.Equal(DocumentFileSaveRejectedConsumer.ErrorCodeStorageRejected, generation.ErrorCode);
        Assert.Contains("UploadPolicy", generation.ErrorMessage);
        Assert.Equal(1, unitOfWork.SaveCount);

        var failed = Assert.IsType<DocumentGenerationFailedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(generation.Id, failed.GenerationId);
        Assert.Equal(externalRef, failed.ExternalReferenceId);
        Assert.Equal("client_form.submission_summary", failed.DocumentType);
        Assert.Equal(TenantId, failed.TenantId);
        Assert.Equal(DocumentFileSaveRejectedConsumer.ErrorCodeStorageRejected, failed.ErrorCode);
    }

    [Fact]
    public async Task Redelivery_of_an_own_rejection_is_a_no_op()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        var repository = new InMemoryRepository(generation);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(fileId), repository, unitOfWork, bus);
        await HandleAsync(Rejected(fileId), repository, unitOfWork, bus);

        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task Rejection_of_an_older_file_leaves_the_retried_generation_untouched()
    {
        var oldFileId = Guid.NewGuid();
        var generation = UploadingGeneration(Guid.NewGuid());
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(oldFileId), new InMemoryRepository(generation), unitOfWork, bus);

        Assert.Equal(DocumentGenerationStatus.Uploading, generation.Status);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Rejection_requested_by_another_service_is_ignored()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(fileId, "signature"), new InMemoryRepository(generation), unitOfWork, bus);

        Assert.Equal(DocumentGenerationStatus.Uploading, generation.Status);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Unknown_file_id_is_a_no_op()
    {
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(Guid.NewGuid()), new InMemoryRepository(), unitOfWork, bus);

        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Rejection_from_another_tenant_is_ignored()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(
            Rejected(fileId) with
            {
                TenantId = Guid.NewGuid(),
            },
            new InMemoryRepository(generation),
            unitOfWork,
            bus
        );

        Assert.Equal(DocumentGenerationStatus.Uploading, generation.Status);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Rejected_signature_copy_is_detached_without_failing_the_generation()
    {
        var copyFileId = Guid.NewGuid();
        var generation = UploadingGeneration(Guid.NewGuid());
        generation.AttachSignatureCopy(copyFileId, 2, CopyOwnerType, CopyFolderType);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(copyFileId), new InMemoryRepository(generation), unitOfWork, bus);

        Assert.Equal(DocumentGenerationStatus.Uploading, generation.Status);
        Assert.Null(generation.SignatureCopyFileId);
        Assert.Null(generation.SignatureAnchorPage);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Signature_copy_of_a_completed_generation_is_kept()
    {
        var fileId = Guid.NewGuid();
        var copyFileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        generation.AttachSignatureCopy(copyFileId, 1, CopyOwnerType, CopyFolderType);
        var now = DateTime.UtcNow;
        Assert.True(generation.MarkStored(new StorageReference(fileId, "application/pdf", 10, null), now).IsSuccess);
        Assert.True(generation.Complete(now).IsSuccess);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(copyFileId), new InMemoryRepository(generation), unitOfWork, bus);

        Assert.Equal(DocumentGenerationStatus.Completed, generation.Status);
        Assert.Equal(copyFileId, generation.SignatureCopyFileId);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Main_rejection_with_a_stored_copy_detaches_it_and_requests_its_deletion()
    {
        var fileId = Guid.NewGuid();
        var copyFileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        generation.AttachSignatureCopy(copyFileId, 2, CopyOwnerType, CopyFolderType);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(fileId), new InMemoryRepository(generation), unitOfWork, bus);

        Assert.Equal(DocumentGenerationStatus.Failed, generation.Status);
        Assert.Null(generation.SignatureCopyFileId);
        Assert.Null(generation.SignatureCopyOwnerType);
        Assert.Null(generation.SignatureCopyFolderType);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Single(bus.Published.OfType<DocumentGenerationFailedIntegrationEvent>());

        var deletion = Assert.Single(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
        Assert.Equal(TenantId, deletion.TenantId);
        Assert.Equal("documents", deletion.SourceService);
        Assert.Equal(generation.Id, deletion.SourceReferenceId);
        Assert.Equal(DocumentFileSaveRejectedConsumer.CopyDeletionReason, deletion.Reason);
        var target = Assert.Single(deletion.Files);
        Assert.Equal(copyFileId, target.FileId);
        Assert.Equal(CopyOwnerType, target.ExpectedOwnerType);
        Assert.Equal(generation.Owner.OwnerId, target.ExpectedOwnerId);
        Assert.Equal(CopyFolderType, target.ExpectedFolderType);
    }

    [Fact]
    public async Task Main_rejection_without_a_copy_requests_no_deletion()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(fileId), new InMemoryRepository(generation), new RecordingUnitOfWork(), bus);

        Assert.Equal(DocumentGenerationStatus.Failed, generation.Status);
        Assert.Empty(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task Replayed_main_rejection_requests_the_copy_deletion_once()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        generation.AttachSignatureCopy(Guid.NewGuid(), 1, CopyOwnerType, CopyFolderType);
        var repository = new InMemoryRepository(generation);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(fileId), repository, unitOfWork, bus);
        await HandleAsync(Rejected(fileId), repository, unitOfWork, bus);

        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Single(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
        Assert.Single(bus.Published.OfType<DocumentGenerationFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Copy_without_recorded_owner_is_detached_but_not_deleted()
    {
        var fileId = Guid.NewGuid();
        var generation = UploadingGeneration(fileId);
        generation.AttachSignatureCopy(Guid.NewGuid(), 1, "", "");
        var bus = new RecordingMessageBus();

        await HandleAsync(Rejected(fileId), new InMemoryRepository(generation), new RecordingUnitOfWork(), bus);

        Assert.Null(generation.SignatureCopyFileId);
        Assert.Empty(bus.Published.OfType<FilesDeletionRequestedIntegrationEvent>());
    }

    private static Task HandleAsync(
        FileSaveRejectedIntegrationEvent evt,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus
    ) =>
        DocumentFileSaveRejectedConsumer.Handle(
            evt,
            repository,
            unitOfWork,
            bus,
            new TestCorrelationContext("corr-1"),
            TimeProvider.System,
            NullLogger<FileSaveRejectedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private static FileSaveRejectedIntegrationEvent Rejected(Guid fileId, string requestingService = "documents") =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-1",
            FileId = fileId,
            RequestingService = requestingService,
            OwnerType = "Customer",
            OwnerId = Guid.NewGuid(),
            FolderType = "ClientForms",
            Reason = "UploadPolicy",
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
            string idempotencyKey,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _generations.FirstOrDefault(g => g.TenantId == tenantId && g.IdempotencyKey == idempotencyKey)
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
