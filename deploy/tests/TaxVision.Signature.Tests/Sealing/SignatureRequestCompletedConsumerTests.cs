using System.Reflection;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Delivery;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// SIG-2: el sellado distingue el fallo transitorio (se reintenta) del permanente (se avisa sin perder lo
/// que sí quedó sellado). SIG-3: sin candado se reintenta y cada ejecución sella todo lo listo. SIG-6: la fase A
/// no escribe y la fase B registra y publica junto.
/// </summary>
public sealed class SignatureRequestCompletedConsumerTests
{
    [Fact]
    public async Task TransientStorageFailure_Throws_DoesNotPublishSealingFailed()
    {
        var request = NewCompletedRequest(documents: 1);
        var storage = new FakeStorage { UploadError = new Error(SignatureStorage.TransientCode, "MinIO PUT failed.") };
        var bus = new CapturingBus();

        await Assert.ThrowsAsync<SealingTransientException>(() =>
            SealAsync(request, storage, new FakeSealer(), bus, attempts: 1)
        );

        Assert.Empty(bus.Messages.OfType<SignatureRequestSealingFailedIntegrationEvent>());
        Assert.Null(request.SealingFailedAtUtc);
    }

    [Fact]
    public async Task TransientFailureOnLastAttempt_PublishesSealingFailedAndMarksRequest()
    {
        var request = NewCompletedRequest(documents: 1);
        var storage = new FakeStorage { UploadError = new Error(SignatureStorage.TransientCode, "MinIO PUT failed.") };
        var bus = new CapturingBus();

        await SealAsync(request, storage, new FakeSealer(), bus, attempts: SealingTransientException.FinalAttempt);

        Assert.Single(bus.Messages.OfType<SignatureRequestSealingFailedIntegrationEvent>());
        Assert.NotNull(request.SealingFailedAtUtc);
    }

    [Fact]
    public async Task PermanentFailureOnSecondDocument_PublishesDocumentSealedForFirst()
    {
        var request = NewCompletedRequest(documents: 2);
        var bus = new CapturingBus();
        var sealer = new FakeSealer { FailOnCall = 2 };

        await SealAsync(request, new FakeStorage(), sealer, bus, attempts: 1);

        var sealedEvent = Assert.Single(bus.Messages.OfType<SignatureDocumentSealedIntegrationEvent>());
        Assert.Equal(request.Documents.OrderBy(d => d.Order).First().Id, sealedEvent.DocumentId);
        var failed = Assert.Single(bus.Messages.OfType<SignatureRequestSealingFailedIntegrationEvent>());
        Assert.Equal("Signature.Sealing.InvalidPdf", failed.ErrorCode);
        Assert.NotNull(request.SealingFailedAtUtc);
    }

    // SIG-14: la imagen de firma que CloudStorage no tiene no se espera como "escaneando".
    [Fact]
    public async Task MissingSignatureImage_PublishesSealingFailedWithoutRetry()
    {
        var request = NewCompletedRequest(documents: 1, signatureImageFileId: Guid.NewGuid());
        var storage = new FakeStorage
        {
            FileLookup = Result.Failure<SignatureFileMetadata>(
                new Error(SignatureStorage.FileNotFoundCode, "File not found in CloudStorage.")
            ),
        };
        var bus = new CapturingBus();

        await SealAsync(request, storage, new FakeSealer(), bus, attempts: 1);

        var failed = Assert.Single(bus.Messages.OfType<SignatureRequestSealingFailedIntegrationEvent>());
        Assert.Equal("Signature.Sealing.SignatureImageMissing", failed.ErrorCode);
        Assert.Empty(bus.Messages.OfType<SignatureDocumentSealedIntegrationEvent>());
        Assert.NotNull(request.SealingFailedAtUtc);
    }

    [Fact]
    public async Task SignatureImageStillScanning_ThrowsNotReady()
    {
        var imageFileId = Guid.NewGuid();
        var request = NewCompletedRequest(documents: 1, signatureImageFileId: imageFileId);
        var storage = new FakeStorage
        {
            FileLookup = Result.Success(
                new SignatureFileMetadata(imageFileId, "Scanning", null, "image/png", 10, null)
            ),
        };
        var bus = new CapturingBus();

        var notReady = await Assert.ThrowsAsync<SignatureImageNotReadyException>(() =>
            SealAsync(request, storage, new FakeSealer(), bus, attempts: 1)
        );

        // SIG-BB7: va por la política estándar de BB-7.
        Assert.IsAssignableFrom<DependencyNotReadyException>(notReady);
        Assert.Empty(bus.Messages.OfType<SignatureRequestSealingFailedIntegrationEvent>());
        Assert.Null(request.SealingFailedAtUtc);
    }

    // SIG-3: otro nodo tiene el candado; el mensaje no se confirma en silencio.
    [Fact]
    public async Task LockNotAcquired_ThrowsLockBusy_DoesNotReturnSilently()
    {
        var request = NewCompletedRequest(documents: 1);
        var bus = new CapturingBus();

        var busy = await Assert.ThrowsAsync<SealingLockBusyException>(() =>
            SealAsync(request, new FakeStorage(), new FakeSealer(), bus, attempts: 1, lockAcquired: false)
        );

        // SIG-BB7: va por la política estándar de BB-7.
        Assert.IsAssignableFrom<DependencyNotReadyException>(busy);
        Assert.Empty(bus.Messages);
        Assert.All(request.Documents, document => Assert.Null(document.SealedFileId));
    }

    [Fact]
    public async Task ReadyForSealingOfDocB_AlsoSealsReadyDocA()
    {
        var request = NewCompletedRequest(documents: 2);
        var documentA = request.Documents.OrderBy(d => d.Order).First();
        var documentB = request.Documents.OrderBy(d => d.Order).Last();
        var bus = new CapturingBus();

        await SealAsync(request, new FakeStorage(), new FakeSealer(), bus, attempts: 1, documentIds: [documentB.Id]);

        Assert.NotNull(documentA.SealedFileId);
        Assert.NotNull(documentB.SealedFileId);
        Assert.Equal(
            [documentA.Id, documentB.Id],
            bus.Messages.OfType<SignatureDocumentSealedIntegrationEvent>().Select(e => e.DocumentId)
        );
    }

    [Fact]
    public async Task SealingPhase_OnlyHandsOverTheResults()
    {
        var request = NewCompletedRequest(documents: 1, generateCertificate: true);
        var bus = new CapturingBus();

        await RunSealingPhaseAsync(request, new FakeStorage(), new FakeSealer(), bus, attempts: 1);

        var command = Assert.IsType<PersistSealedDocumentsCommand>(Assert.Single(bus.Messages));
        Assert.Single(command.Documents);
        Assert.Single(command.Certificates);
        Assert.Equal(2, command.FileRequests.Count);
        Assert.All(request.Documents, document => Assert.Null(document.SealedFileId));
        Assert.Null(request.CertificateFileId);
    }

    [Theory]
    [InlineData(typeof(SealCompletedRequestCommand))]
    [InlineData(typeof(SignatureDocumentsReadyForSealingIntegrationEvent))]
    public void SealingPhase_RunsWithoutTransaction(Type message)
    {
        var handler = typeof(SignatureRequestCompletedConsumer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Handle" && method.GetParameters()[0].ParameterType == message);

        Assert.NotNull(handler.GetCustomAttribute<NonTransactionalAttribute>());
    }

    // El evento lo comparte con la auditoría: aquí solo se pide el sellado, dentro de su transacción.
    [Fact]
    public void CompletedEvent_OnlyRequestsTheSealing()
    {
        var documentId = Guid.NewGuid();
        var evt = new SignatureRequestCompletedIntegrationEvent
        {
            TenantId = Guid.NewGuid(),
            CorrelationId = "sealing-tests",
            SignatureRequestId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            CompletedAtUtc = DateTime.UtcNow,
            Documents = [new DocumentHashDescriptor(documentId, Guid.NewGuid(), new string('a', 64))],
            SignerIds = [],
            GenerateCertificate = false,
            Signers = [],
        };

        var command = CompletedRequestSealingConsumer.Handle(evt);

        Assert.Equal(
            new SealCompletedRequestCommand(
                evt.TenantId,
                evt.SignatureRequestId,
                command.DocumentIds,
                evt.CompletedAtUtc,
                evt.CorrelationId,
                evt.EventId
            ),
            command
        );
        Assert.Equal(documentId, Assert.Single(command.DocumentIds));
        Assert.Null(
            typeof(CompletedRequestSealingConsumer)
                .GetMethod(nameof(CompletedRequestSealingConsumer.Handle))!
                .GetCustomAttribute<NonTransactionalAttribute>()
        );
    }

    [Fact]
    public async Task PersistPhase_RequestsEveryFileAndClosesTheRequest()
    {
        var request = NewCompletedRequest(documents: 2, generateCertificate: true);
        var bus = new CapturingBus();

        await SealAsync(request, new FakeStorage(), new FakeSealer(), bus, attempts: 1);

        var expected = request
            .Documents.Select(document => document.SealedFileId!.Value)
            .Append(request.CertificateFileId!.Value)
            .Order();
        Assert.Equal(expected, bus.Messages.OfType<SaveFileRequestedIntegrationEvent>().Select(e => e.FileId).Order());
        Assert.Single(bus.Messages.OfType<SignatureRequestSealingCompletedIntegrationEvent>());
    }

    [Fact]
    public async Task PersistRedelivered_PublishesNothingNew()
    {
        var request = NewCompletedRequest(documents: 1);
        var bus = new CapturingBus();
        await RunSealingPhaseAsync(request, new FakeStorage(), new FakeSealer(), bus, attempts: 1);
        var command = bus.Messages.OfType<PersistSealedDocumentsCommand>().Single();
        bus.Messages.Clear();
        await PersistAsync(request, command, bus);
        bus.Messages.Clear();

        await PersistAsync(request, command, bus);

        Assert.Empty(bus.Messages);
    }

    // Dos corridas con el mismo documento: el archivo guardado manda y su hash sigue siendo el verdadero.
    [Fact]
    public async Task PersistOfALosingRun_KeepsTheStoredFile_AndAsksForTheCertificateAgain()
    {
        var request = NewCompletedRequest(documents: 2, generateCertificate: true);
        var first = request.Documents.OrderBy(d => d.Order).First();
        var second = request.Documents.OrderBy(d => d.Order).Last();
        var winner = SealedResult(first.Id, 'a');
        var loser = SealedResult(first.Id, 'b');
        var other = SealedResult(second.Id, 'c');
        var certificate = Guid.NewGuid();
        var bus = new CapturingBus();
        await PersistAsync(request, Command(request, [winner], []), bus);
        bus.Messages.Clear();

        await PersistAsync(
            request,
            Command(request, [loser, other], [new SealedCertificateResult(null, certificate)]),
            bus
        );

        Assert.Equal(winner.SealedFileId, first.SealedFileId);
        Assert.Equal(winner.HashPost, first.DocumentHashPost?.Value);
        Assert.Equal(other.SealedFileId, second.SealedFileId);
        Assert.Null(request.CertificateFileId);
        Assert.Equal(
            other.SealedFileId,
            Assert.Single(bus.Messages.OfType<SaveFileRequestedIntegrationEvent>()).FileId
        );
        Assert.Single(bus.Messages.OfType<SignatureDocumentsReadyForSealingIntegrationEvent>());
        Assert.Empty(bus.Messages.OfType<SignatureRequestSealingCompletedIntegrationEvent>());
    }

    private static SealedDocumentResult SealedResult(Guid documentId, char hash) =>
        new(documentId, Guid.NewGuid(), new string(hash, 64), DateTime.UtcNow);

    private static PersistSealedDocumentsCommand Command(
        SignatureRequest request,
        IReadOnlyList<SealedDocumentResult> documents,
        IReadOnlyList<SealedCertificateResult> certificates
    ) =>
        new(
            request.TenantId,
            request.Id,
            "sealing-tests",
            documents,
            certificates,
            documents
                .Select(document => document.SealedFileId)
                .Concat(certificates.Select(item => item.CertificateFileId))
                .Select(fileId => FileRequest(request.TenantId, fileId))
                .ToList()
        );

    private static SaveFileRequestedIntegrationEvent FileRequest(Guid tenantId, Guid fileId) =>
        new()
        {
            TenantId = tenantId,
            FileId = fileId,
            RequestingService = SignatureStorage.RequestingService,
            SourceBucket = "temp",
            SourceObjectKey = $"signature/{fileId:N}/file.pdf",
            ActorId = Guid.NewGuid(),
            OwnerType = "Signature",
            FolderType = "Signatures",
            OriginalName = "file.pdf",
            ContentType = "application/pdf",
            SizeBytes = 3,
        };

    // Las dos fases, como las encadena la cola local.
    private static async Task SealAsync(
        SignatureRequest request,
        ISignatureCloudStorageClient storage,
        IDocumentSealingEngine sealer,
        CapturingBus bus,
        int attempts,
        bool lockAcquired = true,
        IReadOnlyList<Guid>? documentIds = null
    )
    {
        await RunSealingPhaseAsync(request, storage, sealer, bus, attempts, lockAcquired, documentIds);
        foreach (var command in bus.Messages.OfType<PersistSealedDocumentsCommand>().ToList())
        {
            bus.Messages.Remove(command);
            await PersistAsync(request, command, bus);
        }
    }

    private static Task PersistAsync(
        SignatureRequest request,
        PersistSealedDocumentsCommand command,
        CapturingBus bus
    ) =>
        PersistSealedDocumentsHandler.Handle(
            command,
            new SingleRequestRepository(request),
            new NoOpUnitOfWork(),
            bus,
            new FixedCorrelation(),
            NullLogger<PersistSealedDocumentsCommand>.Instance,
            CancellationToken.None
        );

    private static Task RunSealingPhaseAsync(
        SignatureRequest request,
        ISignatureCloudStorageClient storage,
        IDocumentSealingEngine sealer,
        CapturingBus bus,
        int attempts,
        bool lockAcquired = true,
        IReadOnlyList<Guid>? documentIds = null
    ) =>
        SignatureRequestCompletedConsumer.Handle(
            new SignatureDocumentsReadyForSealingIntegrationEvent
            {
                TenantId = request.TenantId,
                CorrelationId = "sealing-tests",
                SignatureRequestId = request.Id,
                CreatedByUserId = request.CreatedByUserId,
                DocumentIds = documentIds ?? request.Documents.Select(d => d.Id).ToList(),
                ReadyAtUtc = DateTime.UtcNow,
                IdempotencyKey = "sealing-tests",
            },
            new SingleRequestRepository(request),
            storage,
            new NoFileRefs(),
            new NoBranding(),
            sealer,
            new FixedStampStyle(),
            new FixedCertificateRenderer(),
            new FixedLock(lockAcquired),
            bus,
            new FixedCorrelation(),
            NullLogger<SignatureRequest>.Instance,
            new Envelope { Attempts = attempts },
            CancellationToken.None
        );

    private static SignatureRequest NewCompletedRequest(
        int documents,
        Guid? signatureImageFileId = null,
        bool generateCertificate = false
    )
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(
                limits,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Sealing",
                null,
                "Fiscal",
                72,
                false,
                false,
                generateCertificate
            )
            .Value;
        for (var i = 1; i <= documents; i++)
            request.AddDocument(limits, Guid.NewGuid(), $"Document {i}");
        var signer = request
            .AddSigner(
                limits,
                SignerEmail.Create("s@example.test").Value,
                SignerFullName.Create("Signer One").Value,
                null
            )
            .Value;
        foreach (var document in request.Documents)
        {
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
            Assert.True(
                request
                    .AttachVerifiedDocument(
                        document.Id,
                        VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 5).Value
                    )
                    .IsSuccess
            );
        }
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        var signed = signatureImageFileId is { } imageFileId
            ? request.MarkSignerSigned(
                signer.Id,
                DateTime.UtcNow,
                SignatureCaptureMethod.Drawn,
                null,
                imageFileId,
                "127.0.0.1",
                "tests"
            )
            : request.MarkSignerSigned(signer.Id, DateTime.UtcNow, "127.0.0.1", "tests");
        Assert.True(signed.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Completed, request.Status);
        return request;
    }

    private sealed class FakeStorage : ISignatureCloudStorageClient
    {
        public Error? UploadError { get; init; }
        public Result<SignatureFileMetadata>? FileLookup { get; init; }

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success<byte[]>([1, 2, 3]));

        public Task<Result<Guid>> UploadAsync(
            Guid tenantId,
            SignatureFileUpload upload,
            CancellationToken ct = default
        ) => Task.FromResult(UploadError is { } error ? Result.Failure<Guid>(error) : Result.Success(Guid.NewGuid()));

        public Task<Result<SaveFileRequestedIntegrationEvent>> StageAsync(
            Guid tenantId,
            SignatureFileUpload upload,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                UploadError is { } error
                    ? Result.Failure<SaveFileRequestedIntegrationEvent>(error)
                    : Result.Success(FileRequest(tenantId, Guid.NewGuid()))
            );

        public Task<Result<Uri>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<Guid>> CopyToCustomerAsync(
            Guid tenantId,
            Guid sourceFileId,
            Guid customerId,
            int taxYear,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<SignatureFileMetadata>> GetFileAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) => FileLookup is { } lookup ? Task.FromResult(lookup) : throw new NotSupportedException();
    }

    // PdfSharp lanza System.Exception genérica ante un PDF ilegible.
    private sealed class FakeSealer : IDocumentSealingEngine
    {
        private int _calls;
        public int FailOnCall { get; init; }

        public SealingResult Seal(SealingRequest request)
        {
            if (++_calls == FailOnCall)
                throw new InvalidOperationException("Invalid PDF structure.");
            return new SealingResult([1, 2, (byte)_calls], new string((char)('b' + _calls), 64));
        }

        public CertificateSealInfo? DescribeSealCertificate() => null;
    }

    private sealed class FixedCertificateRenderer : ICertificateOfCompletionRenderer
    {
        public CertificateResult Render(CertificateOfCompletionModel model) => new([9], new string('f', 64));
    }

    private sealed class NoFileRefs : IFileMetadataRefRepository
    {
        public Task<FileMetadataRef?> GetByFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult<FileMetadataRef?>(null);

        public Task AddAsync(FileMetadataRef projection, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoBranding : ITenantBrandingRefRepository
    {
        public Task<TenantBrandingRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantBrandingRef?>(null);

        public Task AddAsync(TenantBrandingRef branding, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TenantBrandingRef>> ListAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantBrandingRef>>([]);

        public Task<TenantLogoRef?> GetLogoAsync(
            Guid tenantId,
            TenantLogoSurface surface,
            CancellationToken ct = default
        ) => Task.FromResult<TenantLogoRef?>(null);

        public Task<IReadOnlyList<TenantLogoRef>> ListLogosAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantLogoRef>>([]);

        public Task AddLogoAsync(TenantLogoRef logo, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedLock(bool acquired) : IDistributedLock
    {
        public Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult<ILockHandle>(new Handle(key, acquired));

        private sealed class Handle(string key, bool acquired) : ILockHandle
        {
            public bool IsAcquired => acquired;
            public string Key => key;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}
