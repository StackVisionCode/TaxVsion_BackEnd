using System.Net.Sockets;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class ScanFileHandlerTests
{
    private const string Sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Clean_ClientForms_file_publishes_context_without_generic_customer_notice()
    {
        var fixture = CreateFixture(FolderType.ClientForms, null);

        await ScanAsync(fixture);

        var available = Assert.Single(fixture.Bus.Published.OfType<FileAvailableIntegrationEvent>());
        Assert.Equal("ClientForms", available.FolderType);
        Assert.Equal("form.pdf", available.OriginalName);
        Assert.Null(available.TaxYear);
        Assert.Equal("Customer", available.OwnerType);
        Assert.Equal(fixture.CustomerId, available.OwnerId);
        Assert.Empty(fixture.Bus.Published.OfType<CustomerDocumentUploadedIntegrationEvent>());
    }

    [Fact]
    public async Task Clean_Documents_file_keeps_the_generic_customer_notice()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);

        await ScanAsync(fixture);

        var available = Assert.Single(fixture.Bus.Published.OfType<FileAvailableIntegrationEvent>());
        Assert.Equal("Documents", available.FolderType);
        Assert.Equal("form.pdf", available.OriginalName);
        Assert.Equal(2026, available.TaxYear);
        Assert.Single(fixture.Bus.Published.OfType<CustomerDocumentUploadedIntegrationEvent>());
    }

    // CST-9: la red contra el aviso doble. Si vuelve la publicación a mano, aquí salen dos.
    [Fact]
    public async Task Handle_Clean_EmitsExactlyOneFileAvailable()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);

        await ScanAsync(fixture);

        var available = Assert.Single(fixture.Bus.Published.OfType<FileAvailableIntegrationEvent>());
        Assert.Equal(fixture.TenantId, available.TenantId);
        Assert.Equal(fixture.File.Id, available.FileId);
        Assert.Equal(fixture.File.ObjectKey, available.ObjectKey);
        Assert.Equal("application/pdf", available.ContentType);
        Assert.Equal(Sha256, available.ChecksumSha256);
        Assert.Equal(fixture.File.SizeBytes, available.SizeBytes);
        Assert.Equal(fixture.File.CreatedBy, available.CreatedBy);
        Assert.Equal(fixture.File.FolderId, available.FolderId);
        Assert.Equal("corr-scan", available.CorrelationId);

        var uploaded = Assert.Single(fixture.Bus.Published.OfType<CustomerDocumentUploadedIntegrationEvent>());
        Assert.Equal(fixture.CustomerId, uploaded.CustomerId);
        Assert.Equal(fixture.File.Id, uploaded.FileId);
        Assert.Equal("corr-scan", uploaded.CorrelationId);
    }

    [Theory]
    [InlineData("infected", "infected", typeof(FileInfectedDetectedIntegrationEvent))]
    [InlineData("policy", "blocked-by-policy", typeof(FileBlockedByPolicyIntegrationEvent))]
    [InlineData("uncertain", "pending-review", typeof(FilePendingReviewIntegrationEvent))]
    [InlineData("rejected", "rejected", typeof(FileScanFailedIntegrationEvent))]
    public async Task Each_verdict_emits_exactly_one_notice_and_audits_its_outcome(
        string verdict,
        string auditOutcome,
        Type notice
    )
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        switch (verdict)
        {
            case "infected":
                fixture.VirusScanner.Result = VirusScanResult.Infected("Eicar");
                break;
            case "policy":
                fixture.ContentScanner.Result = ContentScanResult.PolicyViolation("Blocked");
                break;
            case "uncertain":
                fixture.ContentScanner.Result = ContentScanResult.Uncertain("Doubt");
                break;
            default:
                fixture.Inspector.Result = new InspectedContent("application/octet-stream", Sha256, false, "Mismatch");
                break;
        }

        await ScanAsync(fixture);

        Assert.Single(fixture.Bus.Published, notice.IsInstanceOfType);
        Assert.Empty(fixture.Bus.Published.OfType<FileAvailableIntegrationEvent>());
        Assert.Single(fixture.Audit.Logs, log => log.Action == "scan" && log.Outcome == auditOutcome);
    }

    [Fact]
    public async Task Rejected_content_asks_to_delete_the_object_and_frees_the_reservation()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        fixture.Inspector.Result = new InspectedContent("application/octet-stream", Sha256, false, "Mismatch");
        var reserved = (await fixture.Limits.GetAsync(fixture.TenantId, default))!.ReservedBytes;

        await ScanAsync(fixture);

        var relocation = Assert.Single(fixture.Bus.Published.OfType<RelocateScannedBlobCommand>());
        Assert.Null(relocation.TargetBucket);
        Assert.Equal(
            reserved - fixture.File.SizeBytes,
            (await fixture.Limits.GetAsync(fixture.TenantId, default))!.ReservedBytes
        );
    }

    [Fact]
    public async Task A_save_that_fails_lets_nothing_out()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        fixture.VirusScanner.Result = VirusScanResult.Infected("Eicar");
        fixture.UnitOfWork.FailSaveWhen = () => true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ScanAsync(fixture));

        Assert.Empty(fixture.Bus.Published);
    }

    [Fact]
    public async Task A_file_already_being_scanned_is_left_to_that_scan()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        fixture.Claims.ScansInProgress.Add(fixture.File.Id);

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.PendingScan, fixture.File.Status);
        Assert.Empty(fixture.Bus.Published);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Content_type_rejection_publishes_FileScanFailed_with_owner_context()
    {
        var fixture = CreateFixture(FolderType.ClientForms, null);
        fixture.Inspector.Result = new InspectedContent("application/octet-stream", Sha256, false, "Mismatch");

        await ScanAsync(fixture);

        var failed = Assert.Single(fixture.Bus.Published.OfType<FileScanFailedIntegrationEvent>());
        Assert.Equal("content_type_mismatch", failed.Reason);
        Assert.Equal("Customer", failed.OwnerType);
        Assert.Equal(fixture.CustomerId, failed.OwnerId);
        Assert.Equal("ClientForms", failed.FolderType);
    }

    [Fact]
    public async Task Infected_and_blocked_events_include_owner_context()
    {
        var infectedFixture = CreateFixture(FolderType.ClientForms, null);
        infectedFixture.VirusScanner.Result = VirusScanResult.Infected("Eicar");
        await ScanAsync(infectedFixture);

        var infected = Assert.Single(infectedFixture.Bus.Published.OfType<FileInfectedDetectedIntegrationEvent>());
        Assert.Equal("Customer", infected.OwnerType);
        Assert.Equal(infectedFixture.CustomerId, infected.OwnerId);
        Assert.Equal("ClientForms", infected.FolderType);

        var blockedFixture = CreateFixture(FolderType.ClientForms, null);
        blockedFixture.ContentScanner.Result = ContentScanResult.PolicyViolation("Blocked");
        await ScanAsync(blockedFixture);

        var blocked = Assert.Single(blockedFixture.Bus.Published.OfType<FileBlockedByPolicyIntegrationEvent>());
        Assert.Equal("Customer", blocked.OwnerType);
        Assert.Equal(blockedFixture.CustomerId, blocked.OwnerId);
        Assert.Equal("ClientForms", blocked.FolderType);
        Assert.True(blockedFixture.File.IsLegalHeld); // evidencia: el purgador no la borra
    }

    [Fact]
    public async Task Pending_review_keeps_the_checksum_and_tells_who_uploaded_it()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        fixture.ContentScanner.Result = ContentScanResult.Uncertain("Possible sexual content (score 0.700).");

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.PendingReview, fixture.File.Status);
        Assert.Equal(Sha256, fixture.File.ChecksumSha256);
        Assert.False(fixture.File.IsLegalHeld);
        // CST-3: el paso a cuarentena viaja en un comando que sale después de guardar.
        Assert.Empty(fixture.Storage.Copied);
        var relocation = Assert.Single(fixture.Bus.Published.OfType<RelocateScannedBlobCommand>());
        Assert.Equal(fixture.Options.TempBucket, relocation.SourceBucket);
        Assert.Equal(fixture.Options.QuarantineBucket, relocation.TargetBucket);
        var pending = Assert.Single(fixture.Bus.Published.OfType<FilePendingReviewIntegrationEvent>());
        Assert.Equal(fixture.File.CreatedBy, pending.CreatedBy);
        Assert.Equal("Customer", pending.OwnerType);
        Assert.Equal(fixture.CustomerId, pending.OwnerId);
        Assert.Equal("Documents", pending.FolderType);
        Assert.Equal("application/pdf", fixture.ContentScanner.LastContext!.DetectedContentType);
        Assert.Equal(FolderType.Documents, fixture.ContentScanner.LastContext.FolderType);
    }

    [Fact]
    public async Task Clean_avatar_is_tagged_in_the_main_bucket_so_the_lifecycle_rule_expires_old_versions()
    {
        var fixture = CreateFixture(FolderType.Avatars, null, "avatar.webp", "image/webp", OwnerType.User);
        fixture.Inspector.Result = new InspectedContent("image/webp", Sha256);

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.Available, fixture.File.Status);
        var tagged = Assert.Single(fixture.Storage.Tagged);
        Assert.Equal(fixture.Options.MainBucket, tagged.Bucket);
        Assert.Equal(fixture.File.ObjectKey, tagged.ObjectKey);
        Assert.Equal(StorageObjectTags.AvatarKind, tagged.Tags[StorageObjectTags.KindKey]);
        Assert.Empty(fixture.Bus.Published.OfType<CustomerDocumentUploadedIntegrationEvent>());
    }

    [Fact]
    public async Task Clean_avatar_on_a_plan_without_webp_is_still_available()
    {
        // Cambio de comportamiento intencional: Avatars no se intersecta con el plan (starter sin webp).
        var fixture = CreateFixture(FolderType.Avatars, null, "avatar.webp", "image/webp", OwnerType.User);
        fixture.Inspector.Result = new InspectedContent("image/webp", Sha256);

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.Available, fixture.File.Status);
        Assert.Single(fixture.Bus.Published.OfType<FileAvailableIntegrationEvent>());
    }

    [Fact]
    public async Task Clean_json_transcript_becomes_available()
    {
        // F7: el JSON v2 del transcript worker pasa el chequeo de tipo detectado (.json ↔ application/json).
        var fixture = CreateFixture(
            FolderType.Transcripts,
            null,
            "Meeting_Transcript_2026-10-30.json",
            "application/json",
            OwnerType.Communication
        );
        fixture.Inspector.Result = new InspectedContent("application/json", Sha256);

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.Available, fixture.File.Status);
        Assert.Equal(
            "Transcripts",
            Assert.Single(fixture.Bus.Published.OfType<FileAvailableIntegrationEvent>()).FolderType
        );
        Assert.Empty(fixture.Bus.Published.OfType<FileScanFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Clean_audio_track_sniffed_as_video_webm_becomes_available_on_a_plan_without_audio()
    {
        // La pista declara audio/webm pero el inspector la ve video/webm; RecordingTracks no se intersecta con el plan.
        var fixture = CreateFixture(
            FolderType.RecordingTracks,
            null,
            "Track_user_track.webm",
            "audio/webm",
            OwnerType.Communication
        );
        fixture.Inspector.Result = new InspectedContent("video/webm", Sha256);

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.Available, fixture.File.Status);
        Assert.Equal(
            "RecordingTracks",
            Assert.Single(fixture.Bus.Published.OfType<FileAvailableIntegrationEvent>()).FolderType
        );
    }

    [Fact]
    public async Task Clean_document_is_not_tagged()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);

        await ScanAsync(fixture);

        Assert.Empty(fixture.Storage.Tagged);
    }

    [Fact]
    public async Task Handle_VirusScannerThrowsSocketException_RethrowsAndKeepsScanning()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        fixture.VirusScanner.Throws = new SocketException((int)SocketError.ConnectionRefused);
        var reservedBefore = (await fixture.Limits.GetAsync(fixture.TenantId, CancellationToken.None))!.ReservedBytes;

        await Assert.ThrowsAsync<SocketException>(() => ScanAsync(fixture));

        Assert.Equal(FileStatus.Scanning, fixture.File.Status);
        Assert.Empty(fixture.Bus.Published.OfType<FileScanFailedIntegrationEvent>());
        Assert.Equal(
            reservedBefore,
            (await fixture.Limits.GetAsync(fixture.TenantId, CancellationToken.None))!.ReservedBytes
        );
    }

    [Fact]
    public async Task Handle_ClamAvWithoutVerdict_ThrowsVirusScanUnavailable()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        fixture.VirusScanner.Result = VirusScanResult.Error("UNKNOWN COMMAND");

        await Assert.ThrowsAsync<VirusScanUnavailableException>(() => ScanAsync(fixture));

        Assert.NotEqual(FileStatus.ScanFailed, fixture.File.Status);
        Assert.Empty(fixture.Bus.Published.OfType<FileScanFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_DetectedTypeNotAllowed_MarksScanFailed()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        fixture.Inspector.Result = new InspectedContent("application/x-msdownload", Sha256, false, "Executable.");

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.ScanFailed, fixture.File.Status);
        Assert.Equal("Executable.", fixture.File.ScanReport);
        Assert.Equal(0, (await fixture.Limits.GetAsync(fixture.TenantId, CancellationToken.None))!.ReservedBytes);
        Assert.Equal(
            "content_type_mismatch",
            Assert.Single(fixture.Bus.Published.OfType<FileScanFailedIntegrationEvent>()).Reason
        );
    }

    [Fact]
    public async Task Handle_FileSoftDeletedWhileQueued_ReturnsWithoutThrowing()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        Assert.True(fixture.File.SoftDelete(fixture.Now, TimeSpan.FromDays(30)).IsSuccess);

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.SoftDeleted, fixture.File.Status);
        Assert.Empty(fixture.Bus.Published);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
        Assert.Empty(fixture.Storage.Downloads);
    }

    [Fact]
    public async Task Handle_FileMissing_ReturnsWithoutThrowing()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        fixture.Files.Remove(fixture.File);

        await ScanAsync(fixture);

        Assert.Empty(fixture.Bus.Published);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_FileAlreadyScanFailed_ReturnsWithoutChanges()
    {
        // Un ScanFileCommand tardío no reabre un ScanFailed definitivo (su reserva ya se liberó).
        var fixture = CreateFixture(FolderType.Documents, 2026);
        Assert.True(fixture.File.MarkScanning().IsSuccess);
        Assert.True(
            fixture
                .File.MarkScanFailed("Detected content type is not allowed.", fixture.Now, "content_type_mismatch")
                .IsSuccess
        );

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.ScanFailed, fixture.File.Status);
        Assert.Empty(fixture.Bus.Published);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_InfectedAndFinalSaveFails_SourceBlobIsNotDeleted()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        fixture.VirusScanner.Result = VirusScanResult.Infected("Eicar");
        fixture.UnitOfWork.FailSaveWhen = () => fixture.File.Status == FileStatus.Infected;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ScanAsync(fixture));

        Assert.DoesNotContain((fixture.Options.TempBucket, fixture.File.ObjectKey), fixture.Storage.Deleted);
        Assert.Empty(fixture.Storage.Copied);
    }

    [Fact]
    public async Task Handle_RetryAfterQuarantineMove_FindsObjectInQuarantine()
    {
        // Un intento anterior movió el objeto a cuarentena y perdió la transacción.
        var fixture = CreateFixture(FolderType.Documents, 2026);
        Assert.True(fixture.File.MarkScanning().IsSuccess);
        fixture.Storage.Existing.Clear();
        fixture.Storage.Existing.Add((fixture.Options.QuarantineBucket, fixture.File.ObjectKey));
        fixture.VirusScanner.Result = VirusScanResult.Infected("Eicar");

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.Infected, fixture.File.Status);
        Assert.Equal(
            (fixture.Options.QuarantineBucket, fixture.File.ObjectKey),
            Assert.Single(fixture.Storage.Downloads)
        );
    }

    [Fact]
    public async Task Handle_BlobMissingEverywhere_MarksBlobMissingWithoutThrowing()
    {
        var fixture = CreateFixture(FolderType.Documents, 2026);
        fixture.Storage.Existing.Clear();

        await ScanAsync(fixture);

        Assert.Equal(FileStatus.ScanFailed, fixture.File.Status);
        Assert.StartsWith("blob_missing", fixture.File.ScanReport);
        Assert.Equal(0, (await fixture.Limits.GetAsync(fixture.TenantId, CancellationToken.None))!.ReservedBytes);
        Assert.Equal(
            "blob_missing",
            Assert.Single(fixture.Bus.Published.OfType<FileScanFailedIntegrationEvent>()).Reason
        );
    }

    private static ScanFixture CreateFixture(
        FolderType folderType,
        int? taxYear,
        string originalName = "form.pdf",
        string contentType = "application/pdf",
        OwnerType ownerType = OwnerType.Customer
    )
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var key = ObjectKey
            .Create(
                $"tenants/{tenantId:N}/{ownerType.ToSegment()}/{customerId:N}/{folderType.ToSegment()}/{Guid.NewGuid():N}{Path.GetExtension(originalName)}"
            )
            .Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                ownerType,
                customerId,
                folderType,
                taxYear,
                key,
                originalName,
                contentType,
                4,
                actorId,
                now,
                now.AddHours(1)
            )
            .Value;
        Assert.True(file.MarkPendingScan(DateTime.UtcNow).IsSuccess);

        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var limit = TenantStorageLimit.Create(tenantId, "starter", 100_000_000, 100_000_000);
        Assert.True(limit.Reserve(file.SizeBytes).IsSuccess);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);
        var options = new CloudStorageOptions();
        var storage = new FakeObjectStorage { DownloadContent = [0x25, 0x50, 0x44, 0x46] };
        storage.Existing.Add((options.TempBucket, file.ObjectKey));

        return new ScanFixture(
            tenantId,
            customerId,
            file,
            files,
            limits,
            storage,
            new StubVirusScanner(VirusScanResult.Clean()),
            new StubContentScanner(ContentScanResult.Clean()),
            new StubInspector(new InspectedContent("application/pdf", Sha256)),
            options,
            new FakeMessageBus(),
            now,
            new FakeUnitOfWork()
        );
    }

    // Lo publicado a mano más lo que sale de los eventos de dominio tras el commit. El veredicto va por el
    // handler real, como el InvokeAsync en línea de Wolverine.
    private static async Task ScanAsync(ScanFixture fixture)
    {
        fixture.File.ClearDomainEvents();
        fixture.Bus.OnInvoke = message =>
            RecordScanVerdictHandler.Handle(
                (RecordScanVerdictCommand)message,
                fixture.Files,
                fixture.Limits,
                fixture.Audit,
                fixture.Storage,
                Options.Create(fixture.Options),
                new FakeSystemClock(fixture.Now),
                fixture.UnitOfWork,
                fixture.Bus,
                CancellationToken.None
            );
        await ScanFileHandler.Handle(
            new ScanFileCommand(fixture.TenantId, fixture.File.Id, "corr-scan", fixture.CustomerId),
            fixture.Claims,
            fixture.Files,
            fixture.Limits,
            fixture.Storage,
            fixture.VirusScanner,
            fixture.ContentScanner,
            fixture.Inspector,
            Options.Create(fixture.Options),
            new FakeUploadPolicyResolver(),
            fixture.UnitOfWork,
            fixture.Bus,
            NullLogger<ScanFileCommand>.Instance,
            CancellationToken.None
        );
        await DomainEventReplay.DispatchIntoAsync(fixture.Bus, fixture.File);
    }

    private sealed record ScanFixture(
        Guid TenantId,
        Guid CustomerId,
        FileObject File,
        FakeFileObjectRepository Files,
        FakeStorageLimitRepository Limits,
        FakeObjectStorage Storage,
        StubVirusScanner VirusScanner,
        StubContentScanner ContentScanner,
        StubInspector Inspector,
        CloudStorageOptions Options,
        FakeMessageBus Bus,
        DateTime Now,
        FakeUnitOfWork UnitOfWork
    )
    {
        public FakeStorageAuditRepository Audit { get; } = new();
        public FakeScanClaims Claims { get; } = new();
    }

    private sealed class StubVirusScanner(VirusScanResult result) : IVirusScanner
    {
        public VirusScanResult Result { get; set; } = result;
        public Exception? Throws { get; set; }

        public Task<VirusScanResult> ScanAsync(Stream content, CancellationToken ct) =>
            Throws is null ? Task.FromResult(Result) : Task.FromException<VirusScanResult>(Throws);
    }

    private sealed class StubContentScanner(ContentScanResult result) : IContentScanner
    {
        public ContentScanResult Result { get; set; } = result;
        public ContentScanContext? LastContext { get; private set; }

        public Task<ContentScanResult> ScanAsync(Stream content, ContentScanContext context, CancellationToken ct)
        {
            LastContext = context;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubInspector(InspectedContent result) : IFileContentInspector
    {
        public InspectedContent Result { get; set; } = result;

        public Task<InspectedContent> InspectAsync(Stream content, string originalName, CancellationToken ct) =>
            Task.FromResult(Result);
    }
}
