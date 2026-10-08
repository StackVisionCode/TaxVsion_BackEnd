using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Requests.Public;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using Wolverine;
using Wolverine.Runtime;

namespace TaxVision.Signature.Tests.Application;

public sealed class SealingMultiDocumentTests
{
    [Fact]
    public async Task Each_ready_document_is_sealed_and_only_the_last_one_completes_the_request_pipeline()
    {
        var request = NewRequest(out var firstSigner, out var secondSigner);
        var firstDocument = request.Documents[0];
        var secondDocument = request.Documents[1];
        var repository = new StoredRequestRepository(request);
        var storage = new MemoryStorage(request.Documents.Select(document => document.OriginalFileId));
        var bus = new SealingMessageBus();
        var unitOfWork = new CountingUnitOfWork();
        var correlation = new FixedCorrelationContext("test-correlation");
        var signedAt = DateTime.UtcNow;

        await SubmitAndSeal(
            request,
            firstSigner,
            firstDocument.Id,
            signedAt,
            repository,
            storage,
            unitOfWork,
            bus,
            correlation
        );

        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);
        Assert.NotNull(firstDocument.SealedFileId);
        Assert.Null(secondDocument.SealedFileId);
        Assert.Null(request.CertificateFileId);
        Assert.Single(bus.Messages.OfType<SignatureDocumentSealedIntegrationEvent>());
        Assert.Empty(bus.Messages.OfType<SignatureRequestSealingCompletedIntegrationEvent>());

        await SubmitAndSeal(
            request,
            secondSigner,
            secondDocument.Id,
            signedAt.AddMinutes(1),
            repository,
            storage,
            unitOfWork,
            bus,
            correlation
        );

        Assert.Equal(SignatureRequestStatus.Completed, request.Status);
        Assert.True(request.AllDocumentsSealed());
        Assert.NotNull(request.CertificateFileId);
        var sealedEvents = bus.Messages.OfType<SignatureDocumentSealedIntegrationEvent>().ToList();
        Assert.Equal(2, sealedEvents.Count);
        Assert.Equal([firstDocument.Id, secondDocument.Id], sealedEvents.Select(item => item.DocumentId));
        Assert.Null(sealedEvents[0].CertificateFileId);
        Assert.Equal(request.CertificateFileId, sealedEvents[1].CertificateFileId);
        var completed = Assert.Single(bus.Messages.OfType<SignatureRequestSealingCompletedIntegrationEvent>());
        Assert.Equal(2, completed.DocumentCount);
        Assert.Equal(3, storage.Uploads.Count);
        Assert.Equal(4, unitOfWork.SaveCount);
    }

    private static async Task SubmitAndSeal(
        SignatureRequest request,
        Signer signer,
        Guid documentId,
        DateTime readyAtUtc,
        ISignatureRequestRepository repository,
        ISignatureCloudStorageClient storage,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var result = await SubmitSignatureHandler.Handle(
            new SubmitSignatureCommand(
                "signed-token",
                SignatureCaptureMethod.Typed,
                signer.FullName.Value,
                null,
                "127.0.0.1",
                "tests",
                DocumentIds: [documentId]
            ),
            new FixedSigningTokenService(request, signer, readyAtUtc),
            repository,
            new EmptyDenylist(),
            unitOfWork,
            bus,
            correlation,
            new NoOpCache(),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);

        var readyEvent = Assert.Single(
            ((SealingMessageBus)bus).Messages.OfType<SignatureDocumentsReadyForSealingIntegrationEvent>(),
            item => item.DocumentIds.Contains(documentId)
        );
        await SignatureRequestCompletedConsumer.Handle(
            readyEvent,
            repository,
            storage,
            new EmptyFileMetadataRepository(),
            new EmptyBrandingRepository(),
            new DeterministicSealer(),
            new DeterministicCertificateRenderer(),
            new AcquiredDistributedLock(),
            unitOfWork,
            bus,
            correlation,
            NullLogger<SignatureRequest>.Instance,
            CancellationToken.None
        );
    }

    private static SignatureRequest NewRequest(out Signer firstSigner, out Signer secondSigner)
    {
        var request = SignatureRequest
            .CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "Two-document package", null, "Fiscal", 72, false, false, true)
            .Value;
        request.AddDocument(Guid.NewGuid(), "Federal return");
        request.AddDocument(Guid.NewGuid(), "State return");
        firstSigner = request
            .AddSigner(SignerEmail.Create("first@example.com").Value, SignerFullName.Create("First Signer").Value, null)
            .Value;
        secondSigner = request
            .AddSigner(
                SignerEmail.Create("second@example.com").Value,
                SignerFullName.Create("Second Signer").Value,
                null
            )
            .Value;
        PlaceSignature(request, firstSigner.Id, request.Documents[0].Id);
        PlaceSignature(request, secondSigner.Id, request.Documents[1].Id);
        request.AttachDocumentHash(request.Documents[0].Id, Hash('a'));
        request.AttachDocumentHash(request.Documents[1].Id, Hash('b'));
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        return request;
    }

    private sealed class FixedSigningTokenService(SignatureRequest request, Signer signer, DateTime expiresAtUtc)
        : ISigningTokenService
    {
        public string Issue(SigningTokenPayload payload) => "signed-token";

        public Result<SigningTokenPayload> Verify(string token) =>
            Result.Success(
                new SigningTokenPayload(
                    request.TenantId,
                    request.Id,
                    signer.Id,
                    request.RevocationEpoch,
                    expiresAtUtc.AddHours(1),
                    $"jti-{signer.Id:N}"
                )
            );
    }

    private sealed class EmptyDenylist : IJtiDenylist
    {
        public Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default) => Task.FromResult(false);

        public Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpCache : ISignatureRequestListCacheInvalidator
    {
        public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static void PlaceSignature(SignatureRequest request, Guid signerId, Guid documentId) =>
        Assert.True(
            request
                .PlaceField(
                    signerId,
                    documentId,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );

    private static DocumentHash Hash(char value) => DocumentHash.Create(new string(value, 64)).Value;

    private sealed class StoredRequestRepository(SignatureRequest request) : ISignatureRequestRepository
    {
        public Task<SignatureRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
            Task.FromResult(request.TenantId == tenantId && request.Id == requestId ? request : null);

        public Task AddAsync(SignatureRequest item, CancellationToken ct = default) => Task.CompletedTask;

        public void Remove(SignatureRequest item) { }

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
        ) => Empty();

        public Task<IReadOnlyList<SignatureRequest>> ListStrandedDraftsAsync(
            DateTime createdBeforeUtc,
            CancellationToken ct = default
        ) => Empty();

        public Task<IReadOnlyList<SignatureRequest>> ListExpiredCandidatesAsync(
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Empty();

        public Task<IReadOnlyList<SignatureRequest>> ListStaleUnsentAsync(
            DateTime olderThanUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Empty();

        public Task<IReadOnlyList<SignatureRequest>> ListReminderCandidatesAsync(
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Empty();

        public Task<IReadOnlyList<SignatureRequest>> ListPurgeCandidatesAsync(
            DateTime olderThanUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Empty();

        public Task<IReadOnlyList<SignatureRequest>> ListCompletedWithSealedFileAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Empty();

        public Task<IReadOnlyList<SignatureRequest>> ListScheduledReadyToSendAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Empty();

        private static Task<IReadOnlyList<SignatureRequest>> Empty() =>
            Task.FromResult<IReadOnlyList<SignatureRequest>>([]);
    }

    private sealed class MemoryStorage(IEnumerable<Guid> originalFileIds) : ISignatureCloudStorageClient
    {
        private readonly HashSet<Guid> _originalFileIds = originalFileIds.ToHashSet();
        public List<(Guid FileId, SignatureFileUpload Upload)> Uploads { get; } = [];

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(
                _originalFileIds.Contains(fileId)
                    ? Result.Success<byte[]>([1, 2, 3])
                    : Result.Failure<byte[]>(new Error("Storage.FileMissing", "File not found."))
            );

        public Task<Result<Guid>> UploadAsync(Guid tenantId, SignatureFileUpload upload, CancellationToken ct = default)
        {
            var fileId = Guid.NewGuid();
            Uploads.Add((fileId, upload));
            return Task.FromResult(Result.Success(fileId));
        }

        public Task<Result<string>> CreateDownloadShareLinkAsync(
            Guid tenantId,
            Guid fileId,
            IReadOnlyList<string> recipientEmails,
            DateTime expiresAtUtc,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success("download-token"));

        public Task<Result<SignatureFileMetadata>> GetFileAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class EmptyFileMetadataRepository : IFileMetadataRefRepository
    {
        public Task<FileMetadataRef?> GetByFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult<FileMetadataRef?>(null);

        public Task AddAsync(FileMetadataRef projection, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class EmptyBrandingRepository : ITenantBrandingRefRepository
    {
        public Task<TenantBrandingRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantBrandingRef?>(null);

        public Task AddAsync(TenantBrandingRef branding, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DeterministicSealer : IDocumentSealingEngine
    {
        private int _sequence;

        public SealingResult Seal(SealingRequest request)
        {
            _sequence++;
            return new SealingResult([1, 2, (byte)_sequence], new string((char)('c' + _sequence), 64));
        }
    }

    private sealed class DeterministicCertificateRenderer : ICertificateOfCompletionRenderer
    {
        public CertificateResult Render(CertificateOfCompletionModel model) => new([9, 8, 7], new string('f', 64));
    }

    private sealed class AcquiredDistributedLock : IDistributedLock
    {
        public Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult<ILockHandle>(new AcquiredLockHandle(key));
    }

    private sealed class AcquiredLockHandle(string key) : ILockHandle
    {
        public bool IsAcquired => true;
        public string Key => key;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
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

    private sealed class SealingMessageBus : IMessageBus
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
