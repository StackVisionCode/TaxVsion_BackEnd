using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Application.Requests.Commands.Create;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Settings;
using Wolverine;
using Wolverine.Runtime;

namespace TaxVision.Signature.Tests.Application;

public sealed class CreateMultiDocumentRequestTests
{
    [Fact]
    public async Task Handler_persists_every_document_and_publishes_the_complete_file_set()
    {
        var tenantId = Guid.NewGuid();
        var firstFileId = Guid.NewGuid();
        var secondFileId = Guid.NewGuid();
        var repository = new CapturingRequestRepository();
        var bus = new CapturingMessageBus();
        var files = new AvailableFiles(
            tenantId,
            (firstFileId, new string('a', 64)),
            (secondFileId, new string('b', 64))
        );
        var command = new CreateSignatureRequestCommand(
            tenantId,
            Guid.NewGuid(),
            "2025 filing package",
            null,
            "Fiscal",
            [
                new CreateSignatureRequestDocumentDto(firstFileId, "Federal return", null),
                new CreateSignatureRequestDocumentDto(secondFileId, "State return", "Sign after federal"),
            ],
            72,
            false,
            true,
            true
        );

        var result = await CreateSignatureRequestHandler.Handle(
            command,
            repository,
            files,
            new EmptySettings(),
            new CountingUnitOfWork(),
            bus,
            new FixedCorrelationContext("create-multi-document"),
            new NoOpCache(),
            new PassingCategoryResolver(),
            new UnexpectedStorage(),
            NullLogger<CreateSignatureRequestCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var persisted = Assert.IsType<SignatureRequest>(repository.Added);
        Assert.Equal(2, persisted.Documents.Count);
        Assert.Equal(
            [firstFileId, secondFileId],
            persisted.Documents.OrderBy(x => x.Order).Select(x => x.OriginalFileId)
        );
        Assert.All(persisted.Documents, document => Assert.NotNull(document.DocumentHashPre));
        var created = Assert.Single(bus.Messages.OfType<SignatureRequestCreatedIntegrationEvent>());
        Assert.Equal(2, created.DocumentCount);
        Assert.Equal([firstFileId, secondFileId], created.OriginalFileIds);
    }

    private sealed class CapturingRequestRepository : ISignatureRequestRepository
    {
        public SignatureRequest? Added { get; private set; }

        public Task AddAsync(SignatureRequest request, CancellationToken ct = default)
        {
            Added = request;
            return Task.CompletedTask;
        }

        public Task<SignatureRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
            Task.FromResult<SignatureRequest?>(null);

        public Task<SignatureRequest?> GetBySealedFileIdAsync(
            Guid tenantId,
            Guid sealedFileId,
            CancellationToken ct = default
        ) => Task.FromResult<SignatureRequest?>(null);

        public Task<SignatureRequest?> GetByCertificateFileIdAsync(
            Guid tenantId,
            Guid certificateFileId,
            CancellationToken ct = default
        ) => Task.FromResult<SignatureRequest?>(null);

        public Task<IReadOnlyList<SignatureRequest>> ListDraftsWaitingForFileAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public Task<IReadOnlyList<SignatureRequest>> ListStrandedDraftsAsync(
            DateTime createdBeforeUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public Task<IReadOnlyList<SignatureRequest>> ListExpiredCandidatesAsync(
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public Task<IReadOnlyList<SignatureRequest>> ListStaleUnsentAsync(
            DateTime olderThanUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public Task<IReadOnlyList<SignatureRequest>> ListReminderCandidatesAsync(
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public Task<IReadOnlyList<SignatureRequest>> ListPurgeCandidatesAsync(
            DateTime olderThanUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public Task<IReadOnlyList<SignatureRequest>> ListCompletedWithSealedFileAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public Task<IReadOnlyList<SignatureRequest>> ListScheduledReadyToSendAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public void Remove(SignatureRequest request) { }
    }

    private sealed class AvailableFiles : IFileMetadataRefRepository
    {
        private readonly Dictionary<Guid, FileMetadataRef> _files;

        public AvailableFiles(Guid tenantId, params (Guid FileId, string Hash)[] files) =>
            _files = files.ToDictionary(
                item => item.FileId,
                item =>
                    FileMetadataRef.ForAvailable(
                        tenantId,
                        item.FileId,
                        $"signature/{item.FileId:N}",
                        "application/pdf",
                        10,
                        item.Hash
                    )
            );

        public Task<FileMetadataRef?> GetByFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(_files.GetValueOrDefault(fileId));

        public Task AddAsync(FileMetadataRef projection, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class EmptySettings : ITenantSignatureSettingsRepository
    {
        public Task<TenantSignatureSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantSignatureSettings?>(null);

        public Task AddAsync(TenantSignatureSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class NoOpCache : ISignatureRequestListCacheInvalidator
    {
        public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class PassingCategoryResolver : ISignatureCategoryResolver
    {
        public Task<Result<string>> ResolveAsync(Guid tenantId, string category, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(category));
    }

    private sealed class UnexpectedStorage : ISignatureCloudStorageClient
    {
        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Available local projections must avoid storage download.");

        public Task<Result<Guid>> UploadAsync(
            Guid tenantId,
            SignatureFileUpload upload,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Create must not upload files.");

        public Task<Result<string>> CreateDownloadShareLinkAsync(
            Guid tenantId,
            Guid fileId,
            IReadOnlyList<string> recipientEmails,
            DateTime expiresAtUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<Result<SignatureFileMetadata>> GetFileAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Available local projections must avoid M2M lookup.");
    }

    private sealed class FixedCorrelationContext(string correlationId) : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = correlationId;

        public void Set(string value) => CorrelationId = value;

        public IDisposable Push(string value)
        {
            var previous = CorrelationId;
            CorrelationId = value;
            return new CallbackDisposable(() => CorrelationId = previous);
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }

    private sealed class CapturingMessageBus : IMessageBus
    {
        public List<object> Messages { get; } = [];
        public string? TenantId { get; set; }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            Messages.Add(message!);
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
