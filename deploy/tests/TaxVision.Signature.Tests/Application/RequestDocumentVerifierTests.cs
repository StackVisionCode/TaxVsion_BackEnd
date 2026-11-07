using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Documents.Verification;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Domain.Validation;

namespace TaxVision.Signature.Tests.Application;

/// <summary>
/// La revisión del servidor del PDF original: rechaza sin descargar lo que la metadata ya delata, reutiliza
/// el preflight del mismo contenido, descarga e inspecciona cuando no lo hay, y aplica los límites del tenant.
/// </summary>
public sealed class RequestDocumentVerifierTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task A_file_that_is_not_a_pdf_is_rejected_without_downloading_it()
    {
        var (verifier, request, document, storage, _) = Arrange();

        var result = await verifier.VerifyAsync(request, document.Id, File("image/png"), CancellationToken.None);

        Assert.Equal(DocumentVerificationKind.Rejected, result.Kind);
        Assert.Equal("Signature.DocumentValidation.MimeType", document.RejectionCode);
        Assert.Equal(0, storage.Downloads);
    }

    [Fact]
    public async Task A_file_over_the_tenant_size_limit_is_rejected_without_downloading_it()
    {
        var limits = DocumentLimits.Default().WithMaxPdfBytes(2 * 1024 * 1024).Value;
        var (verifier, request, document, storage, _) = Arrange(limits: limits);

        var result = await verifier.VerifyAsync(
            request,
            document.Id,
            File(sizeBytes: 3 * 1024 * 1024),
            CancellationToken.None
        );

        Assert.Equal(DocumentVerificationKind.Rejected, result.Kind);
        Assert.Equal("Signature.DocumentValidation.SizeTooLarge", document.RejectionCode);
        Assert.Contains("2 MB", document.RejectionReason);
        Assert.Equal(0, storage.Downloads);
    }

    [Fact]
    public async Task The_preflight_of_the_same_content_is_reused_so_the_file_is_not_downloaded_again()
    {
        var (verifier, request, document, storage, validations) = Arrange();
        validations.Seed(Record(pageCount: 6));

        var result = await verifier.VerifyAsync(request, document.Id, File(), CancellationToken.None);

        Assert.Equal(DocumentVerificationKind.Verified, result.Kind);
        Assert.Equal(6, document.PageCount);
        Assert.Equal(Sha, document.DocumentHashPre!.Value);
        Assert.Equal(0, storage.Downloads);
    }

    [Fact]
    public async Task Without_a_preflight_the_file_is_downloaded_inspected_and_remembered()
    {
        var inspection = new PdfInspection([], Sha, 1024, 4, false);
        var (verifier, request, document, storage, validations) = Arrange(inspection: inspection);

        var result = await verifier.VerifyAsync(request, document.Id, File(), CancellationToken.None);

        Assert.Equal(DocumentVerificationKind.Verified, result.Kind);
        Assert.Equal(4, document.PageCount);
        Assert.Equal(1, storage.Downloads);
        var remembered = Assert.Single(validations.Added);
        Assert.Equal(DocumentValidationVerdict.Accepted, remembered.Verdict);
        Assert.Equal(4, remembered.PageCount);
    }

    [Fact]
    public async Task More_pages_than_the_tenant_allows_is_rejected()
    {
        var limits = DocumentLimits.Default().WithMaxPages(5).Value;
        var (verifier, request, document, _, validations) = Arrange(limits: limits);
        validations.Seed(Record(pageCount: 6));

        var result = await verifier.VerifyAsync(request, document.Id, File(), CancellationToken.None);

        Assert.Equal(DocumentVerificationKind.Rejected, result.Kind);
        Assert.Equal("Signature.DocumentValidation.TooManyPages", document.RejectionCode);
        Assert.Equal(DocumentVerificationStatus.Rejected, document.VerificationStatus);
    }

    [Fact]
    public async Task A_pdf_that_already_carries_signatures_is_rejected()
    {
        var inspection = new PdfInspection([], Sha, 1024, 2, HasExistingSignatures: true);
        var (verifier, request, document, _, _) = Arrange(inspection: inspection);

        var result = await verifier.VerifyAsync(request, document.Id, File(), CancellationToken.None);

        Assert.Equal(DocumentVerificationKind.Rejected, result.Kind);
        Assert.Equal("Signature.DocumentValidation.AlreadySigned", document.RejectionCode);
    }

    [Fact]
    public async Task A_corrupted_pdf_is_rejected_with_the_structural_reason()
    {
        var corrupted = new DocumentValidationIssue(
            "Signature.DocumentValidation.Corrupted",
            "PDF integrity check failed."
        );
        var (verifier, request, document, _, _) = Arrange(
            inspection: new PdfInspection([corrupted], Sha, 1024, null, false)
        );

        var result = await verifier.VerifyAsync(request, document.Id, File(), CancellationToken.None);

        Assert.Equal(DocumentVerificationKind.Rejected, result.Kind);
        Assert.Equal("Signature.DocumentValidation.Corrupted", document.RejectionCode);
    }

    [Fact]
    public async Task When_the_download_fails_the_document_stays_pending_to_retry_later()
    {
        var (verifier, request, document, storage, _) = Arrange();
        storage.FailDownload = true;

        var result = await verifier.VerifyAsync(request, document.Id, File(), CancellationToken.None);

        Assert.Equal(DocumentVerificationKind.Deferred, result.Kind);
        Assert.Equal(DocumentVerificationStatus.Pending, document.VerificationStatus);
    }

    [Fact]
    public async Task Content_that_does_not_match_the_cloudstorage_checksum_is_not_trusted()
    {
        var other = new string('b', 64);
        var (verifier, request, document, _, _) = Arrange(inspection: new PdfInspection([], other, 1024, 2, false));

        var result = await verifier.VerifyAsync(request, document.Id, File(), CancellationToken.None);

        Assert.Equal(DocumentVerificationKind.Deferred, result.Kind);
        Assert.Equal(DocumentVerificationStatus.Pending, document.VerificationStatus);
    }

    [Fact]
    public async Task Using_the_projection_defers_while_the_file_is_still_being_scanned()
    {
        var (verifier, request, document, _, _) = Arrange();

        var result = await verifier.VerifyAsync(request, document.Id, CancellationToken.None);

        Assert.Equal(DocumentVerificationKind.Deferred, result.Kind);
    }

    // ------------------------------------------------------------------

    private static (
        RequestDocumentVerifier Verifier,
        SignatureRequest Request,
        RequestDocument Document,
        CountingStorage Storage,
        InMemoryValidations Validations
    ) Arrange(DocumentLimits? limits = null, PdfInspection? inspection = null)
    {
        var request = SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Engagement letter",
                null,
                "Fiscal",
                72,
                false,
                false,
                true
            )
            .Value;
        var document = request.AddDocument(TestSignatureLimits.Default, Guid.NewGuid(), "Engagement letter").Value;
        var storage = new CountingStorage();
        var validations = new InMemoryValidations(request.TenantId);
        var verifier = new RequestDocumentVerifier(
            new NoFiles(),
            validations,
            storage,
            new FixedInspector(inspection ?? new PdfInspection([], Sha, 1024, 1, false)),
            new FixedLimits(limits ?? DocumentLimits.Default()),
            NullLogger<RequestDocumentVerifier>.Instance
        );
        return (verifier, request, document, storage, validations);
    }

    private static VerifiableFile File(string contentType = "application/pdf", long sizeBytes = 1024) =>
        new(Sha, contentType, sizeBytes);

    private static DocumentValidationRecord Record(int pageCount) =>
        DocumentValidationRecord
            .RecordAccepted(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Sha,
                "letter.pdf",
                "application/pdf",
                1024,
                pageCount,
                false
            )
            .Value;

    private sealed class FixedInspector(PdfInspection inspection) : IPdfInspector
    {
        public PdfInspection Inspect(byte[] content, string declaredContentType) => inspection;
    }

    private sealed class FixedLimits(DocumentLimits limits) : IDocumentLimitsResolver
    {
        public Task<DocumentLimits> ResolveAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(limits);
    }

    private sealed class NoFiles : IFileMetadataRefRepository
    {
        public Task<FileMetadataRef?> GetByFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult<FileMetadataRef?>(null);

        public Task AddAsync(FileMetadataRef projection, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryValidations(Guid tenantId) : IDocumentValidationRepository
    {
        private readonly List<DocumentValidationRecord> _seeded = [];
        public List<DocumentValidationRecord> Added { get; } = [];

        // El registro sembrado representa un preflight del mismo tenant y contenido.
        public void Seed(DocumentValidationRecord record) => _seeded.Add(record);

        public Task AddAsync(DocumentValidationRecord record, CancellationToken ct = default)
        {
            Added.Add(record);
            return Task.CompletedTask;
        }

        public Task<DocumentValidationRecord?> FindInspectedAsync(
            Guid tenant,
            string contentSha256,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                tenant == tenantId
                    ? _seeded.LastOrDefault(record => record.ContentSha256 == contentSha256 && record.PageCount != null)
                    : null
            );
    }

    private sealed class CountingStorage : ISignatureCloudStorageClient
    {
        public Task<Result<BuildingBlocks.Messaging.CloudStorageIntegrationEvents.SaveFileRequestedIntegrationEvent>> StageAsync(
            Guid tenantId,
            SignatureFileUpload upload,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public int Downloads { get; private set; }
        public bool FailDownload { get; set; }

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            Downloads++;
            return Task.FromResult(
                FailDownload
                    ? Result.Failure<byte[]>(new Error("Storage.Unavailable", "CloudStorage is down."))
                    : Result.Success(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D })
            );
        }

        public Task<Result<Guid>> CopyToCustomerAsync(
            Guid tenantId,
            Guid sourceFileId,
            Guid customerId,
            int taxYear,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<Uri>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<SignatureFileMetadata>> GetFileAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
