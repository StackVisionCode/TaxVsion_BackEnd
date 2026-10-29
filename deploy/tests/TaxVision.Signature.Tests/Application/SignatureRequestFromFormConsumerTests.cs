using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Application.Requests.Commands.FromForm;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Settings;
using Wolverine;

namespace TaxVision.Signature.Tests.Application;

// P-18 — consumer que crea la firma desde un comando de Tasks: hash, owner/carpeta, prospecto,
// idempotencia, archivo no disponible (reintento) y seguridad (sin bytes de PDF en el contrato).
public sealed class SignatureRequestFromFormConsumerTests
{
    private const string Hash = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2";

    [Fact]
    public async Task Prospect_without_customer_creates_request_and_publishes_created()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var bus = new CapturingBus();
        var command = BuildCommand(tenantId, fileId, customerId: null, autoSend: false);

        await Handle(command, repo, bus, StorageWith(fileId, "Available", Hash, "Signature", "Signatures"));

        Assert.NotNull(repo.Added);
        Assert.Single(repo.Added!.Signers);
        Assert.Null(repo.Added.Signers[0].MappedCustomerId);
        Assert.NotNull(repo.Added.ExternalReference);
        Assert.Equal("tasks.client_forms", repo.Added.ExternalReference!.SourceService);
        Assert.Contains(bus.Messages, m => m is SignatureRequestFromFormCreatedIntegrationEvent);
        Assert.DoesNotContain(bus.Messages, m => m is SignatureRequestFromFormFailedIntegrationEvent);
    }

    [Fact]
    public async Task Hash_mismatch_publishes_failed_and_creates_nothing()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var bus = new CapturingBus();
        var command = BuildCommand(tenantId, fileId, customerId: null, autoSend: false);

        await Handle(
            command,
            repo,
            bus,
            StorageWith(
                fileId,
                "Available",
                "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
                "Signature",
                "Signatures"
            )
        );

        Assert.Null(repo.Added);
        var failed = Assert.IsType<SignatureRequestFromFormFailedIntegrationEvent>(
            Assert.Single(bus.Messages.Where(m => m is SignatureRequestFromFormFailedIntegrationEvent))
        );
        Assert.Equal(SignatureRequestFromFormOutcomes.HashMismatch, failed.ErrorCode);
    }

    [Fact]
    public async Task Owner_or_folder_mismatch_publishes_failed()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var bus = new CapturingBus();
        var command = BuildCommand(tenantId, fileId, customerId: null, autoSend: false);

        await Handle(command, repo, bus, StorageWith(fileId, "Available", Hash, "Customer", "Documents"));

        Assert.Null(repo.Added);
        var failed = Assert.IsType<SignatureRequestFromFormFailedIntegrationEvent>(
            Assert.Single(bus.Messages.Where(m => m is SignatureRequestFromFormFailedIntegrationEvent))
        );
        Assert.Equal(SignatureRequestFromFormOutcomes.OwnerMismatch, failed.ErrorCode);
    }

    [Fact]
    public async Task File_not_available_throws_to_retry()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var bus = new CapturingBus();
        var command = BuildCommand(tenantId, fileId, customerId: null, autoSend: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handle(command, repo, bus, StorageWith(fileId, "Processing", null, "Signature", "Signatures"))
        );
        Assert.Null(repo.Added);
    }

    [Fact]
    public async Task Idempotent_when_already_exists_republishes_created_without_adding()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var command = BuildCommand(tenantId, fileId, customerId: null, autoSend: false);
        var existing = SignatureRequest
            .CreateDraft(tenantId, Guid.NewGuid(), "Existing", null, "Client form", 72, false, true, true)
            .Value;
        var repo = new CapturingRepository { Existing = existing };
        var bus = new CapturingBus();

        await Handle(command, repo, bus, StorageWith(fileId, "Available", Hash, "Signature", "Signatures"));

        Assert.Null(repo.Added);
        Assert.Contains(bus.Messages, m => m is SignatureRequestFromFormCreatedIntegrationEvent);
    }

    [Fact]
    public async Task Client_signer_without_resolvable_email_publishes_failed()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var bus = new CapturingBus();
        var command = BuildCommand(tenantId, fileId, customerId: Guid.NewGuid(), autoSend: false, email: null);

        await Handle(command, repo, bus, StorageWith(fileId, "Available", Hash, "Signature", "Signatures"));

        Assert.Null(repo.Added);
        var failed = Assert.IsType<SignatureRequestFromFormFailedIntegrationEvent>(
            Assert.Single(bus.Messages.Where(m => m is SignatureRequestFromFormFailedIntegrationEvent))
        );
        Assert.Equal(SignatureRequestFromFormOutcomes.SignerEmailUnavailable, failed.ErrorCode);
    }

    [Fact]
    public void Command_contract_carries_no_pdf_bytes()
    {
        Type[] contracts =
        [
            typeof(SignatureRequestFromFormRequestedIntegrationEvent),
            typeof(SignatureRequestFromFormDocument),
            typeof(SignatureRequestFromFormSigner),
            typeof(SignatureRequestFromFormPlacement),
        ];
        foreach (var contract in contracts)
            Assert.DoesNotContain(contract.GetProperties(), p => p.PropertyType == typeof(byte[]));
    }

    private static Task Handle(
        SignatureRequestFromFormRequestedIntegrationEvent command,
        CapturingRepository repo,
        CapturingBus bus,
        ConfiguredStorage storage
    ) =>
        SignatureRequestFromFormRequestedConsumer.Handle(
            command,
            repo,
            new EmptySettings(),
            new PassingCategoryResolver(),
            storage,
            new NoCustomerEmails(),
            new CountingUnitOfWork(),
            bus,
            new FixedCorrelationContext("corr"),
            new NoOpCache(),
            NullLogger<SignatureRequestFromFormRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private static SignatureRequestFromFormRequestedIntegrationEvent BuildCommand(
        Guid tenantId,
        Guid fileId,
        Guid? customerId,
        bool autoSend,
        string? email = "prospect@example.com"
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = "corr",
            RequestId = Guid.NewGuid(),
            IdempotencyKey = "client-form:r1",
            ExternalReference = new SignatureExternalReference
            {
                SourceService = "tasks.client_forms",
                SourceType = "ClientFormRevision",
                SourceId = $"{Guid.NewGuid():N}:r1",
            },
            CreatedByUserId = Guid.NewGuid(),
            Title = "2026 Intake summary",
            Category = null,
            Documents = [new SignatureRequestFromFormDocument { FileId = fileId, ExpectedSha256 = Hash }],
            Signers =
            [
                new SignatureRequestFromFormSigner
                {
                    Email = email,
                    FullName = "Pat Prospect",
                    Phone = null,
                    Language = "En",
                    VerificationMethod = "EmailOtp",
                    CustomerId = customerId,
                },
            ],
            Placement = new SignatureRequestFromFormPlacement
            {
                DocumentIndex = 0,
                Page = 1,
                X = 0.08,
                Y = 0.25,
                W = 0.38,
                H = 0.07,
            },
            AutoSend = autoSend,
        };

    private static ConfiguredStorage StorageWith(
        Guid fileId,
        string status,
        string? checksum,
        string owner,
        string folder
    ) => new(fileId, new SignatureFileMetadata(fileId, status, checksum, "application/pdf", 10, null, owner, folder));

    private sealed class ConfiguredStorage(Guid fileId, SignatureFileMetadata metadata) : ISignatureCloudStorageClient
    {
        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<Guid>> UploadAsync(
            Guid tenantId,
            SignatureFileUpload upload,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<Result<string>> CreateDownloadShareLinkAsync(
            Guid tenantId,
            Guid id,
            IReadOnlyList<string> recipientEmails,
            DateTime expiresAtUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<Result<SignatureFileMetadata>> GetFileAsync(
            Guid tenantId,
            Guid id,
            CancellationToken ct = default
        ) =>
            id == fileId
                ? Task.FromResult(Result.Success(metadata))
                : Task.FromResult(Result.Failure<SignatureFileMetadata>(new Error("x", "not found")));
    }

    private sealed class CapturingRepository : ISignatureRequestRepository
    {
        public SignatureRequest? Added { get; private set; }
        public SignatureRequest? Existing { get; set; }

        public Task AddAsync(SignatureRequest request, CancellationToken ct = default)
        {
            Added = request;
            return Task.CompletedTask;
        }

        public Task<SignatureRequest?> GetByExternalReferenceAsync(
            Guid tenantId,
            string sourceService,
            string sourceId,
            CancellationToken ct = default
        ) => Task.FromResult(Existing);

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

    // Proyección vacía: ningún cliente resuelve su email.
    private sealed class NoCustomerEmails : ICustomerEmailProjectionRepository
    {
        public Task<CustomerEmailProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerEmailProjection?>(null);

        public Task<CustomerEmailProjection?> FindActiveByEmailAsync(
            Guid tenantId,
            string normalizedEmail,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerEmailProjection?>(null);

        public Task AddAsync(CustomerEmailProjection projection, CancellationToken ct = default) =>
            throw new NotImplementedException();
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

    private sealed class CapturingBus : IMessageBus
    {
        public List<object> Messages { get; } = [];
        public string? TenantId { get; set; }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            Messages.Add(message!);
            return ValueTask.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => Task.FromResult((T)(object)Result.Success());

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => Task.FromResult((T)(object)Result.Success());

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
