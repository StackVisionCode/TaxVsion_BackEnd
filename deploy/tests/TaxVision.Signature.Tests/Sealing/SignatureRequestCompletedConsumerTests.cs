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

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// SIG-2: el sellado distingue el fallo transitorio (se reintenta) del permanente (se avisa sin perder lo
/// que sí quedó sellado). SIG-3: sin candado se reintenta y cada ejecución sella todo lo listo.
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

        await Assert.ThrowsAsync<SignatureImageNotReadyException>(() =>
            SealAsync(request, storage, new FakeSealer(), bus, attempts: 1)
        );

        Assert.Empty(bus.Messages.OfType<SignatureRequestSealingFailedIntegrationEvent>());
        Assert.Null(request.SealingFailedAtUtc);
    }

    // SIG-3: otro nodo tiene el candado; el mensaje no se confirma en silencio.
    [Fact]
    public async Task LockNotAcquired_ThrowsLockBusy_DoesNotReturnSilently()
    {
        var request = NewCompletedRequest(documents: 1);
        var bus = new CapturingBus();

        await Assert.ThrowsAsync<SealingLockBusyException>(() =>
            SealAsync(request, new FakeStorage(), new FakeSealer(), bus, attempts: 1, lockAcquired: false)
        );

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

    private static Task SealAsync(
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
            new NoOpUnitOfWork(),
            bus,
            new FixedCorrelation(),
            NullLogger<SignatureRequest>.Instance,
            new Envelope { Attempts = attempts },
            CancellationToken.None
        );

    private static SignatureRequest NewCompletedRequest(int documents, Guid? signatureImageFileId = null)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Sealing", null, "Fiscal", 72, false, false, false)
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
