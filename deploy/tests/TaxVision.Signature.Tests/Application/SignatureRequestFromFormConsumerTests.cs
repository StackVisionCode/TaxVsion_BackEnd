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
using TaxVision.Signature.Application.Requests.Commands.Send;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
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

        await Assert.ThrowsAsync<SignatureCopyNotReadyException>(() =>
            Handle(command, repo, bus, StorageWith(fileId, "Processing", null, "Signature", "Signatures"))
        );
        Assert.Null(repo.Added);
    }

    [Fact]
    public async Task A_copy_with_a_final_rejected_projection_fails_at_once_without_retrying()
    {
        var fileId = Guid.NewGuid();
        var command = BuildCommand(Guid.NewGuid(), fileId, customerId: null, autoSend: false);
        var files = new ProjectionFiles(FileMetadataRef.ForRejected(command.TenantId, fileId, "StorageQuota.Exceeded"));
        var repo = new CapturingRepository();
        var bus = new CapturingBus();

        await Handle(command, repo, bus, new FailingLookup(SignatureStorage.FileNotFoundCode), files, attempt: 1);

        Assert.Null(repo.Added);
        AssertSingleFailed(bus, SignatureRequestFromFormOutcomes.FileNotAvailable);
    }

    [Theory]
    [InlineData("Infected")]
    [InlineData("BlockedByPolicy")]
    [InlineData("ScanFailed")]
    [InlineData("SoftDeleted")]
    public async Task A_copy_storage_will_never_make_available_fails_at_once(string status)
    {
        var fileId = Guid.NewGuid();
        var command = BuildCommand(Guid.NewGuid(), fileId, customerId: null, autoSend: false);
        var bus = new CapturingBus();

        await Handle(
            command,
            new CapturingRepository(),
            bus,
            StorageWith(fileId, status, null, "Signature", "Signatures")
        );

        AssertSingleFailed(bus, SignatureRequestFromFormOutcomes.FileNotAvailable);
    }

    [Fact]
    public async Task An_unregistered_copy_is_retried_before_the_final_attempt()
    {
        var command = BuildCommand(Guid.NewGuid(), Guid.NewGuid(), customerId: null, autoSend: false);
        var bus = new CapturingBus();

        await Assert.ThrowsAsync<SignatureCopyNotReadyException>(() =>
            Handle(
                command,
                new CapturingRepository(),
                bus,
                new FailingLookup(SignatureStorage.FileNotFoundCode),
                new ProjectionFiles(),
                attempt: SignatureRequestFromFormRequestedConsumer.FinalAttempt - 1
            )
        );

        Assert.Empty(bus.Messages);
    }

    [Fact]
    public async Task An_unregistered_copy_on_the_final_attempt_fails_instead_of_dead_lettering()
    {
        var command = BuildCommand(Guid.NewGuid(), Guid.NewGuid(), customerId: null, autoSend: false);
        var repo = new CapturingRepository();
        var bus = new CapturingBus();

        await Handle(
            command,
            repo,
            bus,
            new FailingLookup(SignatureStorage.FileNotFoundCode),
            new ProjectionFiles(),
            attempt: SignatureRequestFromFormRequestedConsumer.FinalAttempt
        );

        Assert.Null(repo.Added);
        AssertSingleFailed(bus, SignatureRequestFromFormOutcomes.FileNotAvailable);
    }

    [Fact]
    public async Task A_copy_still_scanning_on_the_final_attempt_fails_instead_of_dead_lettering()
    {
        var fileId = Guid.NewGuid();
        var command = BuildCommand(Guid.NewGuid(), fileId, customerId: null, autoSend: false);
        var bus = new CapturingBus();

        await Handle(
            command,
            new CapturingRepository(),
            bus,
            StorageWith(fileId, "Scanning", null, "Signature", "Signatures"),
            new ProjectionFiles(),
            attempt: SignatureRequestFromFormRequestedConsumer.FinalAttempt
        );

        AssertSingleFailed(bus, SignatureRequestFromFormOutcomes.FileNotAvailable);
    }

    [Fact]
    public async Task A_storage_outage_keeps_the_standard_retry_path_even_on_the_final_attempt()
    {
        var command = BuildCommand(Guid.NewGuid(), Guid.NewGuid(), customerId: null, autoSend: false);
        var bus = new CapturingBus();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handle(
                command,
                new CapturingRepository(),
                bus,
                new FailingLookup("Signature.Storage.FileLookup"),
                new ProjectionFiles(),
                attempt: SignatureRequestFromFormRequestedConsumer.FinalAttempt
            )
        );

        Assert.Contains("could not be looked up", thrown.Message, StringComparison.Ordinal);
        Assert.Empty(bus.Messages);
    }

    [Fact]
    public void The_final_attempt_follows_the_copy_wait_policy() =>
        Assert.Equal(
            SignatureRequestFromFormRequestedConsumer.CopyWaitCooldowns.Length + 1,
            SignatureRequestFromFormRequestedConsumer.FinalAttempt
        );

    private static void AssertSingleFailed(CapturingBus bus, string errorCode)
    {
        var failed = Assert.IsType<SignatureRequestFromFormFailedIntegrationEvent>(
            Assert.Single(bus.Messages.Where(m => m is SignatureRequestFromFormFailedIntegrationEvent))
        );
        Assert.Equal(errorCode, failed.ErrorCode);
        Assert.DoesNotContain(bus.Messages, m => m is SignatureRequestFromFormCreatedIntegrationEvent);
    }

    [Fact]
    public async Task Idempotent_when_already_exists_republishes_created_without_adding()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var command = BuildCommand(tenantId, fileId, customerId: null, autoSend: false);
        var existing = SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                tenantId,
                Guid.NewGuid(),
                "Existing",
                null,
                "Client form",
                72,
                false,
                true,
                true
            )
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

    // SIG-13: lo que arma hoy la solicitud, antes de separarlo del consumer.
    [Fact]
    public async Task The_request_takes_defaults_signer_and_field_from_the_command()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var command = BuildCommand(tenantId, fileId, customerId: null, autoSend: false);

        await Handle(
            command,
            repo,
            new CapturingBus(),
            StorageWith(fileId, "Available", Hash, "Signature", "Signatures")
        );

        var request = repo.Added!;
        Assert.Equal(tenantId, request.TenantId);
        Assert.Equal(command.CreatedByUserId, request.CreatedByUserId);
        Assert.Equal("2026 Intake summary", request.Title);
        Assert.Equal("Client form", request.Category);
        Assert.False(request.RequiresSequentialSigning);
        Assert.True(request.RequiresConsent);
        Assert.True(request.GenerateCertificate);
        Assert.False(request.SendSealedDocumentToSigners);
        Assert.False(request.SendCertificateToSigners);
        Assert.True(request.AutoRemindersEnabled);
        Assert.Equal(TenantSignatureSettings.DefaultReminderIntervalHours, request.ReminderIntervalHours);
        Assert.Equal(TenantSignatureSettings.DefaultTokenExpirationHours, request.TokenExpirationHours);

        var document = Assert.Single(request.Documents);
        Assert.Equal(fileId, document.OriginalFileId);
        Assert.Equal("2026 Intake summary", document.Title);

        var signer = Assert.Single(request.Signers);
        Assert.Equal("prospect@example.com", signer.Email.Value);
        Assert.Equal("Pat Prospect", signer.FullName.Value);
        Assert.Equal("En", signer.Language);
        Assert.Equal(SignerVerificationMethod.EmailOtp, signer.RequiredVerificationMethod);

        var field = Assert.Single(signer.Fields);
        Assert.Equal(document.Id, field.DocumentId);
        Assert.Equal(SignatureFieldKind.Signature, field.Kind);
        Assert.Equal("Signature", field.Label);
        Assert.True(field.IsRequired);
        Assert.Equal(0.25, field.Position.Y);
        Assert.Equal(0.38, field.Position.Width);
    }

    [Fact]
    public async Task A_known_category_is_the_canonical_one()
    {
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var command = BuildCommand(Guid.NewGuid(), fileId, null, false) with { Category = "fiscal" };

        await Handle(
            command,
            repo,
            new CapturingBus(),
            StorageWith(fileId, "Available", Hash, "Signature", "Signatures"),
            categories: new CanonicalCategory("Fiscal")
        );

        Assert.Equal("Fiscal", repo.Added!.Category);
    }

    [Fact]
    public async Task An_unknown_category_keeps_the_raw_text()
    {
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var command = BuildCommand(Guid.NewGuid(), fileId, null, false) with { Category = "Something else" };

        await Handle(
            command,
            repo,
            new CapturingBus(),
            StorageWith(fileId, "Available", Hash, "Signature", "Signatures"),
            categories: new CanonicalCategory(null)
        );

        Assert.Equal("Something else", repo.Added!.Category);
    }

    [Fact]
    public async Task A_placement_out_of_range_goes_to_the_first_document()
    {
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var command = BuildCommand(Guid.NewGuid(), fileId, null, false);
        command = command with { Placement = command.Placement with { DocumentIndex = 7 } };

        await Handle(
            command,
            repo,
            new CapturingBus(),
            StorageWith(fileId, "Available", Hash, "Signature", "Signatures")
        );

        Assert.Equal(repo.Added!.Documents[0].Id, Assert.Single(repo.Added.Signers[0].Fields).DocumentId);
    }

    [Theory]
    [InlineData("SmsOtp", "+15551234567", SignerVerificationMethod.SmsOtp)]
    [InlineData("emailotp", null, SignerVerificationMethod.EmailOtp)]
    [InlineData("none", null, null)]
    public async Task The_verification_method_is_parsed_loosely(
        string method,
        string? phone,
        SignerVerificationMethod? expected
    )
    {
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var command = WithSigner(
            BuildCommand(Guid.NewGuid(), fileId, null, false),
            s => s with { VerificationMethod = method, Phone = phone }
        );

        await Handle(
            command,
            repo,
            new CapturingBus(),
            StorageWith(fileId, "Available", Hash, "Signature", "Signatures")
        );

        Assert.Equal(expected, repo.Added!.Signers[0].RequiredVerificationMethod);
        Assert.Equal(phone, repo.Added.Signers[0].PhoneNumber?.Value);
    }

    [Theory]
    [InlineData("not-an-email", "Pat Prospect", null)]
    [InlineData("prospect@example.com", "", null)]
    [InlineData("prospect@example.com", "Pat Prospect", "abc")]
    public async Task An_invalid_signer_publishes_failed(string email, string name, string? phone)
    {
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var bus = new CapturingBus();
        var command = WithSigner(
            BuildCommand(Guid.NewGuid(), fileId, null, false),
            s => s with { Email = email, FullName = name, Phone = phone }
        );

        await Handle(command, repo, bus, StorageWith(fileId, "Available", Hash, "Signature", "Signatures"));

        Assert.Null(repo.Added);
        var failed = Assert.IsType<SignatureRequestFromFormFailedIntegrationEvent>(Assert.Single(bus.Messages));
        Assert.Equal(SignatureRequestFromFormOutcomes.InvalidRequest, failed.ErrorCode);
    }

    [Fact]
    public async Task A_client_signer_takes_email_and_missing_name_from_the_projection()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var command = WithSigner(
            BuildCommand(tenantId, fileId, customerId, false, email: null),
            s => s with { FullName = "" }
        );

        await Handle(
            command,
            repo,
            new CapturingBus(),
            StorageWith(fileId, "Available", Hash, "Signature", "Signatures"),
            customerEmails: new SeededCustomerEmails(
                CustomerEmailProjection.ForNewCustomer(tenantId, customerId, "client@example.test", "Client Name")
            )
        );

        var signer = Assert.Single(repo.Added!.Signers);
        Assert.Equal("client@example.test", signer.Email.Value);
        Assert.Equal("Client Name", signer.FullName.Value);
        Assert.Equal(customerId, signer.MappedCustomerId);
    }

    [Fact]
    public async Task A_rejected_document_publishes_failed_and_saves_nothing()
    {
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var bus = new CapturingBus();
        var verifier = new FixedVerifier(
            DocumentVerificationResult.Rejected(DocumentRejection.Create("Signature.Document.Rejected", "bad").Value)
        );

        await Handle(
            BuildCommand(Guid.NewGuid(), fileId, null, false),
            repo,
            bus,
            StorageWith(fileId, "Available", Hash, "Signature", "Signatures"),
            verifier: verifier
        );

        Assert.Null(repo.Added);
        var failed = Assert.IsType<SignatureRequestFromFormFailedIntegrationEvent>(Assert.Single(bus.Messages));
        Assert.Equal(SignatureRequestFromFormOutcomes.DocumentRejected, failed.ErrorCode);
    }

    [Fact]
    public async Task A_document_that_cannot_be_checked_yet_is_retried()
    {
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var bus = new CapturingBus();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handle(
                BuildCommand(Guid.NewGuid(), fileId, null, false),
                repo,
                bus,
                StorageWith(fileId, "Available", Hash, "Signature", "Signatures"),
                verifier: new FixedVerifier(DocumentVerificationResult.Deferred)
            )
        );

        Assert.Null(repo.Added);
        Assert.Empty(bus.Messages);
    }

    [Fact]
    public async Task A_lost_race_republishes_the_winner_and_does_not_auto_send()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var winner = SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                tenantId,
                Guid.NewGuid(),
                "Winner",
                null,
                "Client form",
                72,
                false,
                true,
                true
            )
            .Value;
        var repo = new CapturingRepository { WinnerAfterConflict = winner };
        var bus = new CapturingBus();

        await Handle(
            BuildCommand(tenantId, fileId, null, autoSend: true),
            repo,
            bus,
            StorageWith(fileId, "Available", Hash, "Signature", "Signatures"),
            unitOfWork: new ConflictingUnitOfWork(repo)
        );

        var created = Assert.IsType<SignatureRequestFromFormCreatedIntegrationEvent>(Assert.Single(bus.Messages));
        Assert.Equal(winner.Id, created.SignatureRequestId);
    }

    [Fact]
    public async Task A_conflict_without_a_winner_is_rethrown()
    {
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();

        await Assert.ThrowsAsync<ConflictException>(() =>
            Handle(
                BuildCommand(Guid.NewGuid(), fileId, null, autoSend: true),
                repo,
                new CapturingBus(),
                StorageWith(fileId, "Available", Hash, "Signature", "Signatures"),
                unitOfWork: new ConflictingUnitOfWork(repo)
            )
        );
    }

    [Fact]
    public async Task Auto_send_goes_out_after_created()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var repo = new CapturingRepository();
        var bus = new CapturingBus();

        await Handle(
            BuildCommand(tenantId, fileId, null, autoSend: true),
            repo,
            bus,
            StorageWith(fileId, "Available", Hash, "Signature", "Signatures")
        );

        Assert.IsType<SignatureRequestFromFormCreatedIntegrationEvent>(bus.Messages[0]);
        var send = Assert.IsType<SendSignatureRequestCommand>(bus.Messages[1]);
        Assert.Equal(tenantId, send.TenantId);
        Assert.Equal(repo.Added!.Id, send.SignatureRequestId);
    }

    private static SignatureRequestFromFormRequestedIntegrationEvent WithSigner(
        SignatureRequestFromFormRequestedIntegrationEvent command,
        Func<SignatureRequestFromFormSigner, SignatureRequestFromFormSigner> change
    ) => command with { Signers = [change(command.Signers[0])] };

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
        ISignatureCloudStorageClient storage,
        IFileMetadataRefRepository? files = null,
        int attempt = 1,
        ISignatureCategoryResolver? categories = null,
        IRequestDocumentVerifier? verifier = null,
        ICustomerEmailProjectionRepository? customerEmails = null,
        IUnitOfWork? unitOfWork = null
    ) =>
        SignatureRequestFromFormRequestedConsumer.Handle(
            command,
            repo,
            new SignatureRequestFromFormBuilder(
                new EmptySettings(),
                categories ?? new PassingCategoryResolver(),
                new FixedSignatureLimitsResolver(),
                new FixedSignaturePlatformSettings(),
                customerEmails ?? new NoCustomerEmails()
            ),
            storage,
            verifier ?? AcceptingDocumentVerifier.Instance,
            unitOfWork ?? new CountingUnitOfWork(),
            bus,
            new FixedCorrelationContext("corr"),
            new NoOpCache(),
            files ?? new ProjectionFiles(),
            new Envelope { Attempts = attempt },
            NullLogger<SignatureRequestFromFormRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );

    private sealed class ProjectionFiles(params FileMetadataRef[] seed) : IFileMetadataRefRepository
    {
        public Task<FileMetadataRef?> GetByFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(seed.FirstOrDefault(file => file.TenantId == tenantId && file.FileId == fileId));

        public Task AddAsync(FileMetadataRef projection, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FailingLookup(string errorCode) : ISignatureCloudStorageClient
    {
        public Task<
            Result<BuildingBlocks.Messaging.CloudStorageIntegrationEvents.SaveFileRequestedIntegrationEvent>
        > StageAsync(Guid tenantId, SignatureFileUpload upload, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<SignatureFileMetadata>> GetFileAsync(
            Guid tenantId,
            Guid id,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<SignatureFileMetadata>(new Error(errorCode, "lookup failed")));

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<Guid>> CopyToCustomerAsync(
            Guid tenantId,
            Guid sourceFileId,
            Guid customerId,
            int taxYear,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<Uri>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

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
        public Task<
            Result<BuildingBlocks.Messaging.CloudStorageIntegrationEvents.SaveFileRequestedIntegrationEvent>
        > StageAsync(Guid tenantId, SignatureFileUpload upload, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<Guid>> CopyToCustomerAsync(
            Guid tenantId,
            Guid sourceFileId,
            Guid customerId,
            int taxYear,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<Uri>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<SignatureFileMetadata>> GetFileAsync(
            Guid tenantId,
            Guid id,
            CancellationToken ct = default
        ) =>
            id == fileId
                ? Task.FromResult(Result.Success(metadata))
                : Task.FromResult(Result.Failure<SignatureFileMetadata>(new Error("x", "not found")));
    }

    private sealed class CanonicalCategory(string? canonical) : ISignatureCategoryResolver
    {
        public Task<Result<string>> ResolveAsync(Guid tenantId, string category, CancellationToken ct = default) =>
            Task.FromResult(
                canonical is null
                    ? Result.Failure<string>(new Error("Signature.Category.Unknown", "Unknown."))
                    : Result.Success(canonical)
            );
    }

    private sealed class FixedVerifier(DocumentVerificationResult result) : IRequestDocumentVerifier
    {
        public Task<DocumentVerificationResult> VerifyAsync(
            SignatureRequest request,
            Guid documentId,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<DocumentVerificationResult> VerifyAsync(
            SignatureRequest request,
            Guid documentId,
            VerifiableFile file,
            CancellationToken ct
        ) => Task.FromResult(result);
    }

    private sealed class SeededCustomerEmails(CustomerEmailProjection projection) : ICustomerEmailProjectionRepository
    {
        public Task<CustomerEmailProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerEmailProjection?>(projection);

        public Task<CustomerEmailProjection?> FindActiveByEmailAsync(
            Guid tenantId,
            string normalizedEmail,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(CustomerEmailProjection value, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    // Dos entregas a la vez: el guardado pierde y el ganador ya es visible.
    private sealed class ConflictingUnitOfWork(CapturingRepository repo) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            repo.Existing = repo.WinnerAfterConflict;
            throw new ConflictException("Persistence.ConcurrencyConflict", "Conflict.");
        }
    }

    private sealed class CapturingRepository : ISignatureRequestRepository
    {
        public SignatureRequest? Added { get; private set; }
        public SignatureRequest? Existing { get; set; }
        public SignatureRequest? WinnerAfterConflict { get; init; }

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
            int maxRemindersPerRequest,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public Task<IReadOnlyList<SignatureRequest>> ListPurgeCandidatesAsync(
            Guid tenantId,
            DateTime olderThanUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<SignatureRequest>>([]);

        public Task<IReadOnlyList<SignatureRequest>> ListCompletedWithSeveralCustomerSignersAsync(
            Guid tenantId,
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

        public Task<IReadOnlyList<Guid>> ListTenantIdsWithPurgeEnabledAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
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
