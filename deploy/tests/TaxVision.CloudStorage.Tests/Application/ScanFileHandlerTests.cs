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
        fixture.Options.PlanPolicies["starter"] = new StoragePlanPolicy
        {
            MaxFileSizeBytes = 10L * 1024 * 1024,
            AllowedExtensions = [".jpg", ".jpeg", ".png"],
            AllowedContentTypes = ["image/jpeg", "image/png"],
        };

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
        fixture.Options.PlanPolicies["starter"] = new StoragePlanPolicy
        {
            MaxFileSizeBytes = 10L * 1024 * 1024,
            AllowedExtensions = [".pdf"],
            AllowedContentTypes = ["application/pdf"],
        };

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
        Assert.True(file.MarkPendingScan().IsSuccess);

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
            now
        );
    }

    private static Task ScanAsync(ScanFixture fixture) =>
        ScanFileHandler.Handle(
            new ScanFileCommand(fixture.TenantId, fixture.File.Id, "corr-scan", fixture.CustomerId),
            fixture.Files,
            fixture.Limits,
            new FakeStorageAuditRepository(),
            fixture.Storage,
            fixture.VirusScanner,
            fixture.ContentScanner,
            fixture.Inspector,
            Options.Create(fixture.Options),
            new FakeUploadPolicyResolver(fixture.Options),
            new FakeSystemClock(fixture.Now),
            new FakeUnitOfWork(),
            fixture.Bus,
            NullLogger<ScanFileCommand>.Instance,
            CancellationToken.None
        );

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
        DateTime Now
    );

    private sealed class StubVirusScanner(VirusScanResult result) : IVirusScanner
    {
        public VirusScanResult Result { get; set; } = result;

        public Task<VirusScanResult> ScanAsync(Stream content, CancellationToken ct) => Task.FromResult(Result);
    }

    private sealed class StubContentScanner(ContentScanResult result) : IContentScanner
    {
        public ContentScanResult Result { get; set; } = result;

        public Task<ContentScanResult> ScanAsync(Stream content, ContentScanContext context, CancellationToken ct) =>
            Task.FromResult(Result);
    }

    private sealed class StubInspector(InspectedContent result) : IFileContentInspector
    {
        public InspectedContent Result { get; set; } = result;

        public Task<InspectedContent> InspectAsync(Stream content, string originalName, CancellationToken ct) =>
            Task.FromResult(Result);
    }
}
