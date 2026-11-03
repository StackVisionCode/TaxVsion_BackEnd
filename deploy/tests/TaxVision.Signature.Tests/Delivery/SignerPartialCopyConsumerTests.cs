using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions.Delivery;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Delivery;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Tests.Projections;

namespace TaxVision.Signature.Tests.Delivery;

/// <summary>
/// Copia parcial con un scan ya resuelto: una copia que CloudStorage no aceptó falla sin entregarse, y una
/// imagen de firma rechazada no frena la copia (sello tipográfico, sin redelivery). La espera de lo
/// desconocido la cubre <see cref="PartialCopyScanGateTests"/>.
/// </summary>
public sealed class SignerPartialCopyConsumerTests
{
    [Fact]
    public async Task A_copy_rejected_by_storage_fails_and_is_not_delivered()
    {
        var setup = new Setup(imageFileId: null, uploadedCopyOutcome: FileScanStatus.Rejected);

        await setup.HandleAsync();

        var completion = setup.Signer.DocumentCompletions.Single();
        Assert.Null(completion.PartialCopySentAtUtc);
        Assert.Contains("was not accepted by storage", completion.PartialCopyFailureReason, StringComparison.Ordinal);
        Assert.Contains(SignatureAuditEventKind.PartialCopyFailed, setup.Audit.Kinds);
        Assert.DoesNotContain(SignatureAuditEventKind.PartialCopyDelivered, setup.Audit.Kinds);
        Assert.Empty(setup.Deliveries.All);
        Assert.Equal(1, setup.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_rejected_signature_image_falls_back_to_the_typed_stamp_without_waiting()
    {
        var imageFileId = Guid.NewGuid();
        var setup = new Setup(imageFileId, uploadedCopyOutcome: FileScanStatus.Available);
        await setup.Files.AddAsync(FileMetadataRef.ForRejected(setup.Request.TenantId, imageFileId, "EmptyFile"));

        await setup.HandleAsync();

        var completion = setup.Signer.DocumentCompletions.Single();
        Assert.NotNull(completion.PartialCopySentAtUtc);
        Assert.DoesNotContain(imageFileId, setup.Storage.Downloads);
        Assert.Contains(SignatureAuditEventKind.PartialCopyDelivered, setup.Audit.Kinds);
        Assert.Single(setup.Deliveries.All);
    }

    private sealed class Setup
    {
        public SignatureRequest Request { get; }
        public Signer Signer { get; }
        public InMemoryFiles Files { get; } = new();
        public InMemoryDeliveries Deliveries { get; } = new();
        public RecordingAudit Audit { get; } = new();
        public CapturingBus Bus { get; } = new();
        public SavingUnitOfWork UnitOfWork { get; } = new();
        public CopyStorage Storage { get; }
        private readonly FixedSettings _settings;
        private readonly DateTime _signedAtUtc = DateTime.UtcNow;

        public Setup(Guid? imageFileId, FileScanStatus uploadedCopyOutcome)
        {
            var tenantId = Guid.NewGuid();
            _settings = new FixedSettings(TenantSignatureSettings.CreateForNewTenant(tenantId, "audit-secret").Value);
            var request = SignatureRequest
                .CreateDraft(
                    TestSignatureLimits.Default,
                    tenantId,
                    Guid.NewGuid(),
                    "2025 filing",
                    null,
                    "Fiscal",
                    72,
                    false,
                    false,
                    false
                )
                .Value;
            request.AddDocument(TestSignatureLimits.Default, Guid.NewGuid(), "Federal return");
            var document = request.Documents[0];
            request.AttachVerifiedDocument(
                document.Id,
                VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 3).Value
            );
            var signer = request
                .AddSigner(
                    TestSignatureLimits.Default,
                    SignerEmail.Create("ana@example.com").Value,
                    SignerFullName.Create("Ana Perez").Value,
                    null
                )
                .Value;
            var other = request
                .AddSigner(
                    TestSignatureLimits.Default,
                    SignerEmail.Create("luis@example.com").Value,
                    SignerFullName.Create("Luis Gomez").Value,
                    null
                )
                .Value;
            Place(request, signer.Id, document.Id);
            Place(request, other.Id, document.Id);
            Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
            Assert.True(
                request
                    .MarkSignerSigned(
                        signer.Id,
                        _signedAtUtc,
                        imageFileId is null ? SignatureCaptureMethod.Typed : SignatureCaptureMethod.Drawn,
                        imageFileId is null ? "Ana Perez" : null,
                        imageFileId,
                        null,
                        null
                    )
                    .IsSuccess
            );
            Request = request;
            Signer = signer;
            Storage = new CopyStorage(Files, tenantId, document.OriginalFileId, uploadedCopyOutcome);
        }

        public Task HandleAsync() =>
            SignerPartialCopyConsumer.Handle(
                new SignerPartialCopyRequestedIntegrationEvent
                {
                    TenantId = Request.TenantId,
                    SignatureRequestId = Request.Id,
                    SignerId = Signer.Id,
                    DocumentIds = [Request.Documents[0].Id],
                    SignedAtUtc = _signedAtUtc,
                    IdempotencyKey = "signature.partial_copy:test:v1",
                },
                new SingleRequestRepository(Request),
                Storage,
                Files,
                new FixedRenderer(),
                new FixedStampStyle(),
                Audit,
                new SignedDocumentDeliveryIssuer(
                    Deliveries,
                    _settings,
                    Files,
                    Audit,
                    new ReadableCodec(),
                    Bus,
                    new FixedCorrelation()
                ),
                UnitOfWork,
                new FixedCorrelation(),
                Bus,
                NullLogger<SignerPartialCopyRequestedIntegrationEvent>.Instance,
                CancellationToken.None
            );

        private static void Place(SignatureRequest request, Guid signerId, Guid documentId) =>
            Assert.True(
                request
                    .PlaceField(
                        TestSignatureLimits.MaxFieldsPerSigner,
                        signerId,
                        documentId,
                        SignatureFieldKind.Signature,
                        FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                        null,
                        true
                    )
                    .IsSuccess
            );
    }

    /// <summary>Sube la copia y deja en la proyección el resultado que anunciaría CloudStorage.</summary>
    private sealed class CopyStorage(
        InMemoryFiles files,
        Guid tenantId,
        Guid originalFileId,
        FileScanStatus copyOutcome
    ) : ISignatureCloudStorageClient
    {
        public List<Guid> Downloads { get; } = [];

        public Task<Result<byte[]>> DownloadAsync(Guid tenant, Guid fileId, CancellationToken ct = default)
        {
            Downloads.Add(fileId);
            return Task.FromResult(
                fileId == originalFileId
                    ? Result.Success<byte[]>([1, 2, 3])
                    : Result.Failure<byte[]>(new Error("Storage.FileMissing", "File not found."))
            );
        }

        public async Task<Result<Guid>> UploadAsync(
            Guid tenant,
            SignatureFileUpload upload,
            CancellationToken ct = default
        )
        {
            var fileId = Guid.NewGuid();
            if (copyOutcome == FileScanStatus.Available)
                files.Available(tenantId, fileId, upload.Content.LongLength);
            else
                await files.AddAsync(FileMetadataRef.ForRejected(tenantId, fileId, "StorageQuota.Exceeded"), ct);
            return Result.Success(fileId);
        }

        public Task<Result<Uri>> GetDownloadUrlAsync(Guid tenant, Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<Guid>> CopyToCustomerAsync(
            Guid tenant,
            Guid sourceFileId,
            Guid customerId,
            int taxYear,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<SignatureFileMetadata>> GetFileAsync(
            Guid tenant,
            Guid fileId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FixedRenderer : IPartialCopyRenderer
    {
        public PartialCopyResult Render(PartialCopyRequest request) => new([4, 5, 6], new string('e', 64));
    }
}
