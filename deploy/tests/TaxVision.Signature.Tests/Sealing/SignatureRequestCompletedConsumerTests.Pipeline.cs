using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// SIG-13. Lo que el pipeline de la fase A entrega hoy: qué recibe el motor de sellado, qué se sube y qué lleva el
/// acta. Fija el comportamiento antes de separar el consumer en colaboradores.
/// </summary>
public sealed partial class SignatureRequestCompletedConsumerTests
{
    private static readonly byte[] SignatureImage = [7, 7, 7];

    private static async Task<PersistSealedDocumentsCommand> SealPhaseAsync(
        SignatureRequest request,
        FakeStorage? storage = null,
        FakeSealer? sealer = null,
        IFileMetadataRefRepository? fileRefs = null,
        ITenantBrandingRefRepository? branding = null,
        FixedCertificateRenderer? certificates = null,
        DateTime? readyAtUtc = null,
        int attempts = 1
    )
    {
        var bus = new CapturingBus();
        await RunSealingPhaseAsync(
            request,
            storage ?? new FakeStorage(),
            sealer ?? new FakeSealer(),
            bus,
            attempts,
            fileRefs: fileRefs,
            branding: branding,
            certificates: certificates,
            readyAtUtc: readyAtUtc
        );
        return Assert.IsType<PersistSealedDocumentsCommand>(Assert.Single(bus.Messages));
    }

    private static FileRefs Scanned(SignatureRequest request, Guid fileId, FileMetadataRef projection) =>
        new(request.TenantId, fileId, projection);

    [Fact]
    public async Task Seal_ReceivesTheOriginal_ThePreHash_TheFooter_AndTheStyle()
    {
        var request = NewCompletedRequest(documents: 1);
        var document = request.Documents[0];
        var storage = new FakeStorage();
        storage.Files[document.OriginalFileId] = [4, 5, 6];
        var sealer = new FakeSealer();
        var evidenceAt = new DateTime(2026, 11, 6, 15, 30, 0, DateTimeKind.Utc);

        await SealPhaseAsync(request, storage, sealer, readyAtUtc: evidenceAt);

        var sealing = Assert.Single(sealer.Requests);
        Assert.Equal(new byte[] { 4, 5, 6 }, sealing.OriginalPdfBytes);
        Assert.Equal(new string('a', 64), sealing.DocumentHashPre);
        Assert.Equal("Completed 2026-11-06 15:30 UTC", sealing.AuditFooter);
        Assert.Equal(FixedStampStyle.Default, sealing.Style);
    }

    [Fact]
    public async Task Seal_StampsTheImageOnlyOnSignatureFields_AndTheTextValueOnTextFields()
    {
        var imageFileId = Guid.NewGuid();
        var request = NewRequestWithTextField(imageFileId, out var signer, out var signedAt);
        var storage = new FakeStorage();
        storage.Files[imageFileId] = SignatureImage;
        var sealer = new FakeSealer();

        await SealPhaseAsync(
            request,
            storage,
            sealer,
            fileRefs: Scanned(
                request,
                imageFileId,
                FileMetadataRef.ForAvailable(request.TenantId, imageFileId, "key", "image/png", 3, new string('e', 64))
            )
        );

        var fields = Assert.Single(sealer.Requests).Fields;
        Assert.Equal(2, fields.Count);

        var signature = fields.Single(field => field.Kind == SignatureFieldKind.Signature);
        Assert.Equal(SignatureImage, signature.SignatureImageBytes);
        Assert.Equal("Signer One", signature.SignerDisplayName);
        Assert.Equal(signedAt, signature.SignedAtUtc);
        Assert.Equal(1, signature.Page);
        Assert.Equal(0.1, signature.X);
        Assert.Equal(0.1, signature.Y);
        Assert.Equal(0.2, signature.Width);
        Assert.Equal(0.05, signature.Height);
        Assert.Null(signature.Value);

        var text = fields.Single(field => field.Kind == SignatureFieldKind.Text);
        Assert.Null(text.SignatureImageBytes);
        Assert.Equal("Typed value", text.Value);
        Assert.Equal("Notes", text.Label);
        Assert.Equal(12, text.FontSizePt);
        Assert.Equal(signer.FullName.Value, text.SignerDisplayName);
    }

    [Fact]
    public async Task Seal_StampsPreparerFieldsWithTheirSignature()
    {
        var preparerFile = Guid.NewGuid();
        var request = NewRequestWithPreparer(preparerFile, withIdentity: true);
        var storage = new FakeStorage();
        storage.Files[preparerFile] = [8, 8];
        var sealer = new FakeSealer();

        await SealPhaseAsync(request, storage, sealer);

        var preparer = Assert.Single(
            Assert.Single(sealer.Requests).Fields,
            field => field.SignerDisplayName == "Pat Preparer"
        );
        Assert.Equal(SignatureFieldKind.Signature, preparer.Kind);
        Assert.Equal(new byte[] { 8, 8 }, preparer.SignatureImageBytes);
        Assert.Equal("Paid preparer", preparer.Label);
        Assert.Equal(request.CompletedAtUtc, preparer.SignedAtUtc);
    }

    [Fact]
    public async Task Seal_WithoutPreparerIdentity_UsesTheGenericName_AndASignatureThatFailsFallsBackToType()
    {
        var preparerFile = Guid.NewGuid();
        var request = NewRequestWithPreparer(preparerFile, withIdentity: false);
        var storage = new FakeStorage();
        storage.FailingDownloads.Add(preparerFile);
        var sealer = new FakeSealer();

        var command = await SealPhaseAsync(request, storage, sealer);

        Assert.Null(command.FailureCode);
        var preparer = Assert.Single(
            Assert.Single(sealer.Requests).Fields,
            field => field.SignerDisplayName == "Preparer"
        );
        Assert.Null(preparer.SignatureImageBytes);
    }

    [Theory]
    [InlineData("Infected")]
    [InlineData("Rejected")]
    public async Task ImageThatWillNeverBeClean_SealsWithTheTypographicFallback(string outcome)
    {
        var imageFileId = Guid.NewGuid();
        var request = NewCompletedRequest(documents: 1, signatureImageFileId: imageFileId);
        var projection =
            outcome == "Infected"
                ? FileMetadataRef.ForInfected(request.TenantId, imageFileId, "EICAR")
                : FileMetadataRef.ForRejected(request.TenantId, imageFileId, "blocked");
        var storage = new FakeStorage();
        var sealer = new FakeSealer();

        await SealPhaseAsync(request, storage, sealer, fileRefs: Scanned(request, imageFileId, projection));

        Assert.DoesNotContain(imageFileId, storage.Downloads);
        Assert.Null(Assert.Single(Assert.Single(sealer.Requests).Fields).SignatureImageBytes);
    }

    [Fact]
    public async Task ImageThatTheProjectionMissed_ButCloudStorageHasAvailable_IsUsed()
    {
        var imageFileId = Guid.NewGuid();
        var request = NewCompletedRequest(documents: 1, signatureImageFileId: imageFileId);
        var storage = new FakeStorage
        {
            FileLookup = Result.Success(
                new SignatureFileMetadata(imageFileId, "Available", null, "image/png", 3, null)
            ),
        };
        storage.Files[imageFileId] = SignatureImage;
        var sealer = new FakeSealer();

        await SealPhaseAsync(request, storage, sealer);

        Assert.Equal(SignatureImage, Assert.Single(Assert.Single(sealer.Requests).Fields).SignatureImageBytes);
    }

    [Fact]
    public async Task ImageStuckInScanning_PastTheWait_SealsWithTheFallbackInsteadOfBlocking()
    {
        var imageFileId = Guid.NewGuid();
        var request = NewCompletedRequest(documents: 1, signatureImageFileId: imageFileId);
        var storage = new FakeStorage
        {
            FileLookup = Result.Success(new SignatureFileMetadata(imageFileId, "Scanning", null, "image/png", 3, null)),
        };
        var sealer = new FakeSealer();

        var command = await SealPhaseAsync(
            request,
            storage,
            sealer,
            readyAtUtc: DateTime.UtcNow - TimeSpan.FromMinutes(3)
        );

        Assert.Null(command.FailureCode);
        Assert.Null(Assert.Single(Assert.Single(sealer.Requests).Fields).SignatureImageBytes);
    }

    [Fact]
    public async Task ImageThatFailedScanning_IsADefinitiveFailure()
    {
        var imageFileId = Guid.NewGuid();
        var request = NewCompletedRequest(documents: 1, signatureImageFileId: imageFileId);
        var storage = new FakeStorage
        {
            FileLookup = Result.Success(
                new SignatureFileMetadata(imageFileId, "ScanFailed", null, "image/png", 3, null)
            ),
        };
        var sealer = new FakeSealer();

        var command = await SealPhaseAsync(request, storage, sealer);

        Assert.Equal(SignatureImageScanGate.SignatureImageMissingCode, command.FailureCode);
        Assert.Empty(sealer.Requests);
        Assert.Empty(command.Documents);
    }

    [Fact]
    public async Task AvailableImageThatCannotBeDownloaded_IsRetried()
    {
        var imageFileId = Guid.NewGuid();
        var request = NewCompletedRequest(documents: 1, signatureImageFileId: imageFileId);
        var storage = new FakeStorage();
        storage.FailingDownloads.Add(imageFileId);
        var bus = new CapturingBus();

        await Assert.ThrowsAsync<SignatureImageNotReadyException>(() =>
            RunSealingPhaseAsync(
                request,
                storage,
                new FakeSealer(),
                bus,
                attempts: 1,
                fileRefs: Scanned(
                    request,
                    imageFileId,
                    FileMetadataRef.ForAvailable(
                        request.TenantId,
                        imageFileId,
                        "key",
                        "image/png",
                        3,
                        new string('e', 64)
                    )
                )
            )
        );

        Assert.Empty(bus.Messages);
    }

    [Fact]
    public async Task NetworkFailureInsideTheSealer_IsTransient_AndIsRetried()
    {
        var request = NewCompletedRequest(documents: 1);
        var sealer = new FakeSealer { FailOnCall = 1, Failure = new HttpRequestException("TSA down") };

        await Assert.ThrowsAsync<SealingTransientException>(() =>
            RunSealingPhaseAsync(request, new FakeStorage(), sealer, new CapturingBus(), attempts: 1)
        );
    }

    [Fact]
    public async Task OriginalThatCannotBeDownloaded_IsTransient_ButFinalOnTheLastAttempt()
    {
        var request = NewCompletedRequest(documents: 1);
        var storage = new FakeStorage();
        storage.FailingDownloads.Add(request.Documents[0].OriginalFileId);

        var command = await SealPhaseAsync(request, storage, attempts: SealingTransientException.FinalAttempt);

        Assert.Equal(SignatureStorage.TransientCode, command.FailureCode);
    }

    [Fact]
    public async Task Upload_OfTheSealedDocument_GoesToTheSignatureOwner_WithItsName()
    {
        var request = NewCompletedRequest(documents: 1);
        var storage = new FakeStorage();

        await SealPhaseAsync(request, storage);

        var upload = Assert.Single(storage.Staged);
        Assert.Equal(new byte[] { 1, 2, 1 }, upload.Content);
        Assert.Equal(DocumentFileName.For("Document 1", "_Signed.pdf"), upload.FileName);
        Assert.Equal("application/pdf", upload.ContentType);
        Assert.Equal("Signature", upload.OwnerType);
        Assert.Equal(request.Id, upload.OwnerId);
        Assert.Equal("Signatures", upload.FolderType);
        Assert.Equal(request.CompletedAtUtc!.Value.Year, upload.TaxYear);
        Assert.Equal(request.CreatedByUserId, upload.ActorId);
    }

    [Fact]
    public async Task Certificate_IsRenderedWithTheResultsOfThisRun_AndUploadedAfterTheDocuments()
    {
        var request = NewCompletedRequest(documents: 2, generateCertificate: true);
        var ordered = request.Documents.OrderBy(document => document.Order).ToList();
        var signer = request.Signers[0];
        var renderer = new FixedCertificateRenderer();
        var storage = new FakeStorage();
        var seal = new CertificateSealInfo("CN=Seal", "CN=Issuer", "01", "RSA", DateTime.UtcNow);

        var command = await SealPhaseAsync(
            request,
            storage,
            new FakeSealer { Certificate = seal },
            certificates: renderer
        );

        Assert.Equal(
            [
                DocumentFileName.For("Document 1", "_Signed.pdf"),
                DocumentFileName.For("Document 2", "_Signed.pdf"),
                DocumentFileName.For("Sealing", "_Certificate.pdf"),
            ],
            storage.Staged.Select(upload => upload.FileName)
        );

        var certificate = Assert.Single(command.Certificates);
        Assert.Null(certificate.DocumentId);
        Assert.Equal(3, command.FileRequests.Count);

        var model = Assert.Single(renderer.Models);
        Assert.Equal(request.Id, model.SignatureRequestId);
        Assert.Equal("Sealing", model.Title);
        Assert.Equal("Fiscal", model.Category);
        Assert.Equal(request.CreatedAtUtc, model.CreatedAtUtc);
        Assert.Equal(request.CompletedAtUtc, model.CompletedAtUtc);
        Assert.Same(seal, model.Seal);
        Assert.Null(model.Preparer);
        Assert.Null(model.IssuerName);
        Assert.Null(model.TenantLogo);
        Assert.Null(model.PlatformLogo);

        var entry = Assert.Single(model.SignersGlobal);
        Assert.Equal(signer.Id, entry.SignerId);
        Assert.Equal("Signer One", entry.FullName);
        Assert.Equal("s@example.test", entry.Email);

        Assert.Equal(ordered.Select(document => document.Id), model.Documents.Select(document => document.DocumentId));
        var first = model.Documents[0];
        var sealedFirst = command.Documents.Single(document => document.DocumentId == ordered[0].Id);
        Assert.Equal(new string('a', 64), first.HashPre);
        Assert.Equal(sealedFirst.HashPost, first.HashPost);
        Assert.Equal(sealedFirst.SealedAtUtc, first.SealedAtUtc);
        Assert.Equal(signer.Id, Assert.Single(first.Signers).SignerId);
    }

    [Theory]
    [InlineData(SignatureCaptureMethod.Drawn, "Drawn signature")]
    [InlineData(SignatureCaptureMethod.Uploaded, "Uploaded image")]
    public async Task Certificate_LabelsTheCaptureMethod(SignatureCaptureMethod method, string label)
    {
        var imageFileId = Guid.NewGuid();
        var request = NewCompletedRequest(
            documents: 1,
            generateCertificate: true,
            captureMethod: method,
            signatureImageFileId: imageFileId
        );
        var renderer = new FixedCertificateRenderer();

        await SealPhaseAsync(
            request,
            certificates: renderer,
            fileRefs: Scanned(
                request,
                imageFileId,
                FileMetadataRef.ForAvailable(request.TenantId, imageFileId, "key", "image/png", 3, new string('e', 64))
            )
        );

        Assert.Equal(label, Assert.Single(Assert.Single(renderer.Models).SignersGlobal).CaptureMethod);
    }

    [Fact]
    public async Task Certificate_ReferencesThePreparerWithAMaskedIdentifier()
    {
        var preparerFile = Guid.NewGuid();
        var request = NewRequestWithPreparer(preparerFile, withIdentity: true, generateCertificate: true);
        var renderer = new FixedCertificateRenderer();

        await SealPhaseAsync(request, certificates: renderer);

        var preparer = Assert.Single(renderer.Models).Preparer!;
        Assert.Equal("Pat Preparer", preparer.DisplayName);
        Assert.Equal("P••••5678", preparer.MaskedIdentifier);
        Assert.Equal(request.CompletedAtUtc, preparer.SignedAtUtc);
    }

    [Fact]
    public async Task Certificate_WithoutPreparerIdentity_ListsNoPreparer()
    {
        var request = NewRequestWithPreparer(Guid.NewGuid(), withIdentity: false, generateCertificate: true);
        var renderer = new FixedCertificateRenderer();

        await SealPhaseAsync(request, certificates: renderer);

        Assert.Null(Assert.Single(renderer.Models).Preparer);
    }

    [Fact]
    public async Task Certificate_UsesThePortalLogoOfTheOffice_AndTheCrmLogoOfThePlatform()
    {
        var request = NewCompletedRequest(documents: 1, generateCertificate: true);
        var officePortal = Guid.NewGuid();
        var officeCrm = Guid.NewGuid();
        var platformCrm = Guid.NewGuid();
        var storage = new FakeStorage();
        storage.Files[officePortal] = [1];
        storage.Files[platformCrm] = [2];
        var branding = new Logos(
            Logo(request.TenantId, TenantLogoSurface.Portal, officePortal),
            Logo(request.TenantId, TenantLogoSurface.Crm, officeCrm),
            Logo(PlatformTenant.Id, TenantLogoSurface.Crm, platformCrm)
        );
        var renderer = new FixedCertificateRenderer();

        await SealPhaseAsync(request, storage, branding: branding, certificates: renderer);

        var model = Assert.Single(renderer.Models);
        Assert.Equal(new byte[] { 1 }, model.TenantLogo);
        Assert.Equal(new byte[] { 2 }, model.PlatformLogo);
        Assert.DoesNotContain(officeCrm, storage.Downloads);
    }

    [Fact]
    public async Task Certificate_ALogoThatDoesNotDownload_TriesTheNextSurface_AndNeverStopsTheCertificate()
    {
        var request = NewCompletedRequest(documents: 1, generateCertificate: true);
        var officePortal = Guid.NewGuid();
        var officeCrm = Guid.NewGuid();
        var storage = new FakeStorage();
        storage.FailingDownloads.Add(officePortal);
        storage.Files[officeCrm] = [3];
        var renderer = new FixedCertificateRenderer();

        var command = await SealPhaseAsync(
            request,
            storage,
            branding: new Logos(
                Logo(request.TenantId, TenantLogoSurface.Portal, officePortal),
                Logo(request.TenantId, TenantLogoSurface.Crm, officeCrm)
            ),
            certificates: renderer
        );

        Assert.Null(command.FailureCode);
        Assert.Equal(new byte[] { 3 }, Assert.Single(renderer.Models).TenantLogo);
    }

    [Fact]
    public async Task Certificate_PerDocumentMode_OneCertificatePerDocument_WithOnlyItsSigners()
    {
        var request = NewCompletedRequest(
            documents: 2,
            generateCertificate: true,
            certificateMode: CertificateGenerationMode.PerDocument
        );
        var ordered = request.Documents.OrderBy(document => document.Order).ToList();
        var renderer = new FixedCertificateRenderer();
        var storage = new FakeStorage();

        var command = await SealPhaseAsync(request, storage, certificates: renderer);

        Assert.Equal(
            ordered.Select(document => (Guid?)document.Id),
            command.Certificates.Select(item => item.DocumentId)
        );
        Assert.Equal(["Document 1", "Document 2"], renderer.Models.Select(model => model.Title));
        Assert.All(renderer.Models, model => Assert.Single(model.Documents));
        Assert.EndsWith("_Certificate.pdf", storage.Staged[^1].FileName);
    }

    [Fact]
    public async Task Certificate_IsNotRequestedWhenTheRequestDoesNotAskForIt()
    {
        var request = NewCompletedRequest(documents: 1, generateCertificate: false);
        var renderer = new FixedCertificateRenderer();

        var command = await SealPhaseAsync(request, certificates: renderer);

        Assert.Empty(command.Certificates);
        Assert.Empty(renderer.Models);
    }

    [Fact]
    public async Task Certificate_ThatCannotBeUploaded_KeepsTheSealedDocumentsInTheResult()
    {
        var request = NewCompletedRequest(documents: 1, generateCertificate: true);
        var storage = new FakeStorage { FailOnStage = 2 };

        var command = await SealPhaseAsync(request, storage);

        Assert.Equal("Signature.Storage.Refused", command.FailureCode);
        Assert.Single(command.Documents);
        Assert.Empty(command.Certificates);
    }

    [Fact]
    public async Task InProgressRequest_SealsTheReadyDocument_WithoutACertificate()
    {
        var request = NewRequestWithOneDocumentSigned(out var signedDocument);
        var renderer = new FixedCertificateRenderer();
        var bus = new CapturingBus();

        await RunSealingPhaseAsync(
            request,
            new FakeStorage(),
            new FakeSealer(),
            bus,
            attempts: 1,
            documentIds: [signedDocument],
            certificates: renderer
        );

        var command = Assert.IsType<PersistSealedDocumentsCommand>(Assert.Single(bus.Messages));
        Assert.Equal(signedDocument, Assert.Single(command.Documents).DocumentId);
        Assert.Empty(command.Certificates);
        Assert.Empty(renderer.Models);
    }

    [Fact]
    public async Task UnknownRequest_IsSkippedWithoutPublishing()
    {
        var request = NewCompletedRequest(documents: 1);
        var bus = new CapturingBus();

        await RunSealingPhaseAsync(
            request,
            new FakeStorage(),
            new FakeSealer(),
            bus,
            attempts: 1,
            repository: new SingleRequestRepository(NewCompletedRequest(documents: 1))
        );

        Assert.Empty(bus.Messages);
    }

    [Fact]
    public async Task RequestThatCannotSeal_IsSkippedWithoutPublishing()
    {
        var limits = TestSignatureLimits.Default;
        var draft = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Draft", null, "Fiscal", 72, false, false, false)
            .Value;
        draft.AddDocument(limits, Guid.NewGuid(), "Document 1");
        var bus = new CapturingBus();

        await RunSealingPhaseAsync(draft, new FakeStorage(), new FakeSealer(), bus, attempts: 1);

        Assert.Empty(bus.Messages);
    }

    [Fact]
    public async Task EverythingAlreadySealed_IsSkippedWithoutPublishing()
    {
        var request = NewCompletedRequest(documents: 1);
        var bus = new CapturingBus();
        await SealAsync(request, new FakeStorage(), new FakeSealer(), bus, attempts: 1);
        bus.Messages.Clear();

        await RunSealingPhaseAsync(request, new FakeStorage(), new FakeSealer(), bus, attempts: 1);

        Assert.Empty(bus.Messages);
    }

    private static TenantLogoRef Logo(Guid tenantId, TenantLogoSurface surface, Guid fileId)
    {
        var logo = TenantLogoRef.Create(tenantId, surface, DateTime.UtcNow.AddMinutes(-1));
        Assert.True(logo.SetLogo(fileId, "image/png", 1, DateTime.UtcNow));
        return logo;
    }

    private static SignatureRequest NewRequestWithTextField(Guid imageFileId, out Signer signer, out DateTime signedAt)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Sealing", null, "Fiscal", 72, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Document 1").Value;
        signer = request
            .AddSigner(
                limits,
                SignerEmail.Create("s@example.test").Value,
                SignerFullName.Create("Signer One").Value,
                null
            )
            .Value;
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
        var text = request
            .PlaceField(
                TestSignatureLimits.MaxFieldsPerSigner,
                signer.Id,
                document.Id,
                SignatureFieldKind.Text,
                FieldPosition.Create(1, 0.3, 0.3, 0.2, 0.05).Value,
                "Notes",
                false,
                12
            )
            .Value;
        Assert.True(
            request
                .AttachVerifiedDocument(
                    document.Id,
                    VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 5).Value
                )
                .IsSuccess
        );
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        signedAt = DateTime.UtcNow;
        Assert.True(
            request
                .CaptureSignerFieldValues(
                    signer.Id,
                    [document.Id],
                    [new SignerFieldValueInput(text.Id, "Typed value")],
                    signedAt
                )
                .IsSuccess
        );
        Assert.True(
            request
                .MarkSignerSigned(
                    signer.Id,
                    signedAt,
                    SignatureCaptureMethod.Drawn,
                    null,
                    imageFileId,
                    "127.0.0.1",
                    "tests"
                )
                .IsSuccess
        );
        return request;
    }

    private static SignatureRequest NewRequestWithPreparer(
        Guid preparerFile,
        bool withIdentity,
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
        var document = request.AddDocument(limits, Guid.NewGuid(), "Document 1").Value;
        var signer = request
            .AddSigner(
                limits,
                SignerEmail.Create("s@example.test").Value,
                SignerFullName.Create("Signer One").Value,
                null
            )
            .Value;
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
                .PlacePreparerField(
                    document.Id,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.5, 0.5, 0.2, 0.05).Value,
                    "Paid preparer"
                )
                .IsSuccess
        );
        Assert.True(request.SetPreparerSignature(preparerFile).IsSuccess);
        if (withIdentity)
            Assert.True(request.SetPreparer(PreparerInfo.Create("P12345678", "Pat Preparer", null).Value).IsSuccess);
        Assert.True(
            request
                .AttachVerifiedDocument(
                    document.Id,
                    VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 5).Value
                )
                .IsSuccess
        );
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        Assert.True(request.MarkSignerSigned(signer.Id, DateTime.UtcNow, "127.0.0.1", "tests").IsSuccess);
        return request;
    }

    // Dos firmantes, cada uno en su documento; solo firma el primero: la solicitud sigue InProgress.
    private static SignatureRequest NewRequestWithOneDocumentSigned(out Guid signedDocument)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Sealing", null, "Fiscal", 72, false, false, true)
            .Value;
        var first = request.AddDocument(limits, Guid.NewGuid(), "Document 1").Value;
        var second = request.AddDocument(limits, Guid.NewGuid(), "Document 2").Value;
        var one = request
            .AddSigner(
                limits,
                SignerEmail.Create("a@example.test").Value,
                SignerFullName.Create("Signer One").Value,
                null
            )
            .Value;
        var two = request
            .AddSigner(
                limits,
                SignerEmail.Create("b@example.test").Value,
                SignerFullName.Create("Signer Two").Value,
                null
            )
            .Value;
        foreach (var (signer, document) in new[] { (one, first), (two, second) })
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
        Assert.True(request.MarkSignerSigned(one.Id, DateTime.UtcNow, "127.0.0.1", "tests").IsSuccess);
        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);
        signedDocument = first.Id;
        return request;
    }

    private sealed class FileRefs(Guid tenantId, Guid fileId, FileMetadataRef projection) : IFileMetadataRefRepository
    {
        public Task<FileMetadataRef?> GetByFileIdAsync(Guid tenant, Guid file, CancellationToken ct = default) =>
            Task.FromResult(tenant == tenantId && file == fileId ? projection : null);

        public Task AddAsync(FileMetadataRef value, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Logos(params TenantLogoRef[] logos) : ITenantBrandingRefRepository
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
        ) => Task.FromResult(logos.FirstOrDefault(logo => logo.TenantId == tenantId && logo.Surface == surface));

        public Task<IReadOnlyList<TenantLogoRef>> ListLogosAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantLogoRef>>(logos.Where(logo => logo.TenantId == tenantId).ToList());

        public Task AddLogoAsync(TenantLogoRef logo, CancellationToken ct = default) => Task.CompletedTask;
    }
}
