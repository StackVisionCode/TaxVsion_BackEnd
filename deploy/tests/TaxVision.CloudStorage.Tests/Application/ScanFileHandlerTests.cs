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

    private static ScanFixture CreateFixture(FolderType folderType, int? taxYear)
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var key = ObjectKey
            .Create($"tenants/{tenantId:N}/customer/{customerId:N}/{folderType.ToSegment()}/{Guid.NewGuid():N}.pdf")
            .Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Customer,
                customerId,
                folderType,
                taxYear,
                key,
                "form.pdf",
                "application/pdf",
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
