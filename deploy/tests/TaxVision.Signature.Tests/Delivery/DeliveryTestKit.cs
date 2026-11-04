using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Delivery;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Delivery;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;
using Wolverine;

namespace TaxVision.Signature.Tests.Delivery;

/// <summary>Solicitud completada y sellada con dos documentos y dos firmantes, más los dobles de la entrega.</summary>
internal sealed class DeliveryTestKit
{
    public SignatureRequest Request { get; }
    public Signer FirstSigner { get; }
    public Signer SecondSigner { get; }
    public InMemoryDeliveries Deliveries { get; } = new();
    public InMemoryFiles Files { get; } = new();
    public FixedSettings Settings { get; }
    public RecordingAudit Audit { get; } = new();
    public ReadableCodec Codec { get; } = new();
    public CapturingBus Bus { get; } = new();
    public RecordingStorage Storage { get; } = new();

    public DeliveryTestKit(
        bool sendSealed = true,
        bool sendCertificate = false,
        CertificateGenerationMode certificateMode = CertificateGenerationMode.PerDocument,
        DeliveryPolicy? policy = null,
        Guid? firstSignerCustomerId = null,
        Guid? secondSignerCustomerId = null
    )
    {
        var tenantId = Guid.NewGuid();
        var settings = TenantSignatureSettings.CreateForNewTenant(tenantId, "audit-secret").Value;
        if (policy is not null)
            settings.ReplaceDeliveryPolicy(policy);
        Settings = new FixedSettings(settings);

        var request = SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                tenantId,
                Guid.NewGuid(),
                "2025 filing package",
                null,
                "Fiscal",
                72,
                false,
                false,
                generateCertificate: sendCertificate,
                sendSealedDocumentToSigners: sendSealed,
                sendCertificateToSigners: sendCertificate,
                certificateGenerationMode: certificateMode
            )
            .Value;
        request.AddDocument(TestSignatureLimits.Default, Guid.NewGuid(), "Federal return");
        request.AddDocument(TestSignatureLimits.Default, Guid.NewGuid(), "State return");
        foreach (var document in request.Documents)
            request.AttachVerifiedDocument(
                document.Id,
                VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 3).Value
            );

        FirstSigner = request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create("ana@example.com").Value,
                SignerFullName.Create("Ana Perez").Value,
                firstSignerCustomerId
            )
            .Value;
        SecondSigner = request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create("luis@example.com").Value,
                SignerFullName.Create("Luis Gomez").Value,
                secondSignerCustomerId
            )
            .Value;
        Place(request, FirstSigner, request.Documents[0]);
        Place(request, SecondSigner, request.Documents[1]);
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        Assert.True(request.MarkSignerSigned(FirstSigner.Id, DateTime.UtcNow, null, null).IsSuccess);
        Assert.True(request.MarkSignerSigned(SecondSigner.Id, DateTime.UtcNow, null, null).IsSuccess);
        Request = request;
    }

    /// <summary>Sella los documentos (y certificados si la solicitud los genera) y deja sus archivos Available.</summary>
    public DeliveryTestKit Seal(long sizeBytes = 1024, bool certificatesAvailable = true)
    {
        foreach (var document in Request.Documents)
        {
            var sealedId = Guid.NewGuid();
            Assert.True(
                Request
                    .MarkDocumentSealed(
                        document.Id,
                        sealedId,
                        DocumentHash.Create(new string('b', 64)).Value,
                        DateTime.UtcNow
                    )
                    .IsSuccess
            );
            Files.Available(Request.TenantId, sealedId, sizeBytes);
        }

        if (!Request.GenerateCertificate)
            return this;

        if (Request.CertificateGenerationMode == CertificateGenerationMode.SingleForRequest)
        {
            var certificateId = Guid.NewGuid();
            Assert.True(Request.RecordCertificate(certificateId).IsSuccess);
            if (certificatesAvailable)
                Files.Available(Request.TenantId, certificateId, sizeBytes);
        }
        else
            foreach (var document in Request.Documents)
            {
                var certificateId = Guid.NewGuid();
                Assert.True(Request.RecordDocumentCertificate(document.Id, certificateId).IsSuccess);
                if (certificatesAvailable)
                    Files.Available(Request.TenantId, certificateId, sizeBytes);
            }
        return this;
    }

    public DeliverableFiles DeliverableFiles() => new(Files, Storage);

    public SignedDocumentDeliveryIssuer Issuer() =>
        new(Deliveries, Settings, Files, Audit, Codec, Bus, new FixedCorrelation());

    private static void Place(SignatureRequest request, Signer signer, RequestDocument document) =>
        Assert.True(
            request
                .PlaceField(
                    TestSignatureLimits.MaxFieldsPerSigner,
                    signer.Id,
                    document.Id,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );
}

internal sealed class InMemoryDeliveries : ISignedDocumentDeliveryRepository
{
    public List<SignedDocumentDelivery> All { get; } = [];

    public Task AddAsync(SignedDocumentDelivery delivery, CancellationToken ct = default)
    {
        All.Add(delivery);
        return Task.CompletedTask;
    }

    public Task<SignedDocumentDelivery?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(All.SingleOrDefault(delivery => delivery.TokenHash == tokenHash));

    public Task<SignedDocumentDelivery?> GetByIdAsync(Guid tenantId, Guid deliveryId, CancellationToken ct = default) =>
        Task.FromResult(All.SingleOrDefault(delivery => delivery.TenantId == tenantId && delivery.Id == deliveryId));

    public Task<IReadOnlyList<SignedDocumentDelivery>> ListByRequestAsync(
        Guid tenantId,
        Guid signatureRequestId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SignedDocumentDelivery>>(
            All.Where(delivery => delivery.TenantId == tenantId && delivery.SignatureRequestId == signatureRequestId)
                .ToList()
        );

    public Task<bool> HasCompletedDeliveryAsync(
        Guid tenantId,
        Guid signatureRequestId,
        Guid signerId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            All.Any(delivery =>
                delivery.TenantId == tenantId
                && delivery.SignatureRequestId == signatureRequestId
                && delivery.SignerId == signerId
                && delivery.Purpose == DeliveryPurpose.Completed
            )
        );
}

internal sealed class InMemoryFiles : IFileMetadataRefRepository
{
    private readonly List<FileMetadataRef> _files = [];

    public void Available(Guid tenantId, Guid fileId, long sizeBytes) =>
        _files.Add(
            FileMetadataRef.ForAvailable(
                tenantId,
                fileId,
                $"k/{fileId}",
                "application/pdf",
                sizeBytes,
                new string('c', 64)
            )
        );

    public Task<FileMetadataRef?> GetByFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(_files.SingleOrDefault(file => file.TenantId == tenantId && file.FileId == fileId));

    public Task AddAsync(FileMetadataRef projection, CancellationToken ct = default)
    {
        _files.Add(projection);
        return Task.CompletedTask;
    }
}

internal sealed class FixedSettings(TenantSignatureSettings settings) : ITenantSignatureSettingsRepository
{
    public TenantSignatureSettings Value => settings;

    public Task<TenantSignatureSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<TenantSignatureSettings?>(settings);

    public Task AddAsync(TenantSignatureSettings value, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<Guid>> ListTenantIdsWithPurgeEnabledAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);
}

internal sealed class RecordingAudit : IAuditChainAppender
{
    public List<SignatureAuditEventKind> Kinds { get; } = [];

    public Task<Result<SignatureAuditEvent>> AppendAsync(
        Guid tenantId,
        Guid signatureRequestId,
        SignatureAuditEventKind kind,
        DateTime occurredAtUtc,
        object payload,
        CancellationToken ct = default
    )
    {
        Kinds.Add(kind);
        return Task.FromResult(Result.Failure<SignatureAuditEvent>(new Error("Test.Audit", "not persisted in tests")));
    }
}

/// <summary>"Cifra" dejando el token legible, atado al esquema y a la entrega, para poder afirmarlo.</summary>
internal sealed class ReadableCodec : IProtectedPayloadCodec
{
    public ProtectedPayload Protect<T>(string schema, ProtectedPayloadBinding binding, T content) =>
        new()
        {
            Cipher =
                $"{binding.TenantId:N}|{binding.IdempotencyKey}|{System.Text.Json.JsonSerializer.Serialize(content)}",
            KeyVersion = 1,
            Schema = schema,
        };

    public T Unprotect<T>(ProtectedPayload payload, string expectedSchema, ProtectedPayloadBinding expectedBinding)
    {
        var parts = payload.Cipher.Split('|', 3);
        if (
            payload.Schema != expectedSchema
            || parts[0] != expectedBinding.TenantId.ToString("N")
            || parts[1] != expectedBinding.IdempotencyKey
        )
            throw new InvalidOperationException("binding mismatch");
        return System.Text.Json.JsonSerializer.Deserialize<T>(parts[2])!;
    }
}

internal sealed class RecordingStorage : ISignatureCloudStorageClient
{
    public List<Guid> Presigned { get; } = [];
    public List<(Guid Source, Guid Customer)> Copies { get; } = [];
    public HashSet<Guid> FailCopiesOf { get; } = [];

    public Task<Result<Guid>> CopyToCustomerAsync(
        Guid tenantId,
        Guid sourceFileId,
        Guid customerId,
        int taxYear,
        CancellationToken ct = default
    )
    {
        if (FailCopiesOf.Contains(sourceFileId))
            return Task.FromResult(Result.Failure<Guid>(new Error("Storage.Copy", "copy failed")));
        Copies.Add((sourceFileId, customerId));
        return Task.FromResult(Result.Success(Guid.NewGuid()));
    }

    public Task<Result<Uri>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        Presigned.Add(fileId);
        return Task.FromResult(Result.Success(new Uri($"https://minio.test/{fileId}")));
    }

    public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Result<Guid>> UploadAsync(Guid tenantId, SignatureFileUpload upload, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <summary>Estado de cada archivo en CloudStorage (la fuente); los que no estén aquí no existen.</summary>
    public Dictionary<Guid, string> RemoteStatuses { get; } = [];

    public Task<Result<SignatureFileMetadata>> GetFileAsync(
        Guid tenantId,
        Guid fileId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            RemoteStatuses.TryGetValue(fileId, out var status)
                ? Result.Success(new SignatureFileMetadata(fileId, status, null, "application/pdf", 1024, null))
                : Result.Failure<SignatureFileMetadata>(new Error("File.NotFound", "The file does not exist."))
        );
}

internal sealed class FixedCorrelation : ICorrelationContext
{
    public string CorrelationId => "delivery-test";

    public IDisposable Push(string correlationId) => new Noop();

    public void Set(string correlationId) { }

    private sealed class Noop : IDisposable
    {
        public void Dispose() { }
    }
}

internal sealed class CapturingBus : IMessageBus
{
    public List<object> Messages { get; } = [];
    public string? TenantId { get; set; }

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        Messages.Add(message!);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotSupportedException();

    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
        throw new NotSupportedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotSupportedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        throw new NotSupportedException();

    public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotSupportedException();

    public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotSupportedException();

    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotSupportedException();

    public Task<T> InvokeAsync<T>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task InvokeForTenantAsync(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public Task<T> InvokeForTenantAsync<T>(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        CancellationToken cancellation = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default
    ) => throw new NotSupportedException();
}

/// <summary>Repositorio con una sola solicitud (copiado del de los tests de sellado).</summary>
internal sealed class SingleRequestRepository(SignatureRequest request) : ISignatureRequestRepository
{
    public Task<SignatureRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
        Task.FromResult(request.TenantId == tenantId && request.Id == requestId ? request : null);

    public Task AddAsync(SignatureRequest item, CancellationToken ct = default) => Task.CompletedTask;

    public void Remove(SignatureRequest item) { }

    public Task<SignatureRequest?> GetByExternalReferenceAsync(
        Guid tenantId,
        string sourceService,
        string sourceId,
        CancellationToken ct = default
    ) => Task.FromResult<SignatureRequest?>(null);

    public Task<SignatureRequest?> GetBySealedFileIdAsync(
        Guid tenantId,
        Guid sealedFileId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            request.TenantId == tenantId && request.Documents.Any(document => document.SealedFileId == sealedFileId)
                ? request
                : null
        );

    public Task<SignatureRequest?> GetByCertificateFileIdAsync(
        Guid tenantId,
        Guid certificateFileId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            request.TenantId == tenantId
            && (
                request.CertificateFileId == certificateFileId
                || request.Documents.Any(document => document.CertificateFileId == certificateFileId)
            )
                ? request
                : null
        );

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
        int maxRemindersPerRequest,
        CancellationToken ct = default
    ) => Empty();

    public Task<IReadOnlyList<SignatureRequest>> ListPurgeCandidatesAsync(
        Guid tenantId,
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    ) => Empty();

    public Task<IReadOnlyList<SignatureRequest>> ListCompletedWithSeveralCustomerSignersAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<SignatureRequest>>(request.TenantId == tenantId ? [request] : []);

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
