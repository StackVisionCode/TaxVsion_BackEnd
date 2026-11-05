using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class RescueStuckScanHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_PendingScanWithBlobInTemp_RequeuesOnce()
    {
        var fixture = CreateFixture();
        fixture.Storage.Existing.Add((fixture.Options.TempBucket, fixture.File.ObjectKey));

        await RescueAsync(fixture);

        var requeued = Assert.Single(fixture.Bus.Published.OfType<ScanFileCommand>());
        Assert.Equal(fixture.File.Id, requeued.FileId);
        Assert.Equal(fixture.File.TenantId, requeued.TenantId);
        Assert.Equal(1, fixture.File.ScanAttemptCount);
        Assert.Equal(Now, fixture.File.ScanQueuedAtUtc);
        Assert.Equal(FileStatus.PendingScan, fixture.File.Status);
        Assert.Equal(fixture.File.SizeBytes, fixture.Limit.ReservedBytes);
        Assert.Empty(fixture.Bus.Published.OfType<FileScanFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_ScanningWithBlobInMain_Requeues()
    {
        var fixture = CreateFixture();
        Assert.True(fixture.File.MarkScanning().IsSuccess);
        fixture.Storage.Existing.Add((fixture.Options.MainBucket, fixture.File.ObjectKey));

        await RescueAsync(fixture);

        Assert.Single(fixture.Bus.Published.OfType<ScanFileCommand>());
        Assert.Equal(1, fixture.File.ScanAttemptCount);
    }

    [Fact]
    public async Task Handle_ScanningWithBlobOnlyInQuarantine_Requeues()
    {
        // CST-3: un intento anterior pudo dejar el objeto en cuarentena antes de perder la transacción.
        var fixture = CreateFixture();
        Assert.True(fixture.File.MarkScanning().IsSuccess);
        fixture.Storage.Existing.Add((fixture.Options.QuarantineBucket, fixture.File.ObjectKey));

        await RescueAsync(fixture);

        Assert.Single(fixture.Bus.Published.OfType<ScanFileCommand>());
        Assert.Empty(fixture.Bus.Published.OfType<FileScanFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_BlobMissingEverywhere_MarksScanFailedAndPublishesBlobMissing()
    {
        var fixture = CreateFixture();
        Assert.True(fixture.File.MarkScanning().IsSuccess);

        await RescueAsync(fixture);

        Assert.Equal(FileStatus.ScanFailed, fixture.File.Status);
        Assert.StartsWith("blob_missing", fixture.File.ScanReport);
        Assert.Equal(0, fixture.Limit.ReservedBytes);
        var failed = Assert.Single(fixture.Bus.Published.OfType<FileScanFailedIntegrationEvent>());
        Assert.Equal("blob_missing", failed.Reason);
        Assert.Equal(fixture.File.Id, failed.FileId);
        Assert.Empty(fixture.Bus.Published.OfType<ScanFileCommand>());
        var log = Assert.Single(fixture.Audit.Logs);
        Assert.Equal("scan.rescue", log.Action);
        Assert.Equal("irrecoverable", log.Outcome);
    }

    [Fact]
    public async Task Handle_AttemptsExhausted_MarksScanStalled()
    {
        var fixture = CreateFixture();
        fixture.Storage.Existing.Add((fixture.Options.TempBucket, fixture.File.ObjectKey));
        for (var i = 0; i < 3; i++)
            Assert.True(fixture.File.RequeueScan(Now.AddHours(-3), maxAttempts: 3).IsSuccess);

        await RescueAsync(fixture);

        Assert.Equal(FileStatus.ScanFailed, fixture.File.Status);
        Assert.StartsWith("scan_stalled", fixture.File.ScanReport);
        Assert.Equal(0, fixture.Limit.ReservedBytes);
        Assert.Equal(
            "scan_stalled",
            Assert.Single(fixture.Bus.Published.OfType<FileScanFailedIntegrationEvent>()).Reason
        );
        Assert.Empty(fixture.Bus.Published.OfType<ScanFileCommand>());
    }

    [Fact]
    public async Task Handle_FileAlreadyAvailable_DoesNothing()
    {
        var fixture = CreateFixture();
        Assert.True(fixture.File.MarkScanning().IsSuccess);
        Assert.True(
            fixture
                .File.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", Now)
                .IsSuccess
        );
        fixture.Storage.Existing.Add((fixture.Options.MainBucket, fixture.File.ObjectKey));

        await RescueAsync(fixture);

        Assert.Empty(fixture.Bus.Published);
        Assert.Empty(fixture.Audit.Logs);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
        Assert.Equal(FileStatus.Available, fixture.File.Status);
        Assert.Equal(0, fixture.File.ScanAttemptCount);
    }

    [Fact]
    public async Task Handle_FileMissing_DoesNothing()
    {
        var fixture = CreateFixture();

        await RescueStuckScanHandler.Handle(
            new RescueStuckScanCommand(fixture.File.TenantId, Guid.NewGuid()),
            fixture.Files,
            fixture.Limits,
            fixture.Audit,
            fixture.Storage,
            Options.Create(fixture.Options),
            new FakeSystemClock(Now),
            fixture.UnitOfWork,
            fixture.Bus,
            NullLogger<RescueStuckScanCommand>.Instance,
            CancellationToken.None
        );

        Assert.Empty(fixture.Bus.Published);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    private static RescueFixture CreateFixture()
    {
        var tenantId = Guid.NewGuid();
        var createdAt = Now.AddHours(-3);
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2026/{Guid.NewGuid():N}.pdf").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                "stuck.pdf",
                "application/pdf",
                40,
                Guid.NewGuid(),
                createdAt,
                createdAt.AddHours(1)
            )
            .Value;
        Assert.True(file.MarkPendingScan(createdAt).IsSuccess);

        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var limit = TenantStorageLimit.Create(tenantId, "starter", 100_000_000, 100_000_000);
        Assert.True(limit.Reserve(file.SizeBytes).IsSuccess);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);

        return new RescueFixture(
            file,
            files,
            limit,
            limits,
            new FakeStorageAuditRepository(),
            new FakeObjectStorage(),
            new CloudStorageOptions(),
            new FakeUnitOfWork(),
            new FakeMessageBus()
        );
    }

    private static Task RescueAsync(RescueFixture fixture) =>
        RescueStuckScanHandler.Handle(
            new RescueStuckScanCommand(fixture.File.TenantId, fixture.File.Id),
            fixture.Files,
            fixture.Limits,
            fixture.Audit,
            fixture.Storage,
            Options.Create(fixture.Options),
            new FakeSystemClock(Now),
            fixture.UnitOfWork,
            fixture.Bus,
            NullLogger<RescueStuckScanCommand>.Instance,
            CancellationToken.None
        );

    private sealed record RescueFixture(
        FileObject File,
        FakeFileObjectRepository Files,
        TenantStorageLimit Limit,
        FakeStorageLimitRepository Limits,
        FakeStorageAuditRepository Audit,
        FakeObjectStorage Storage,
        CloudStorageOptions Options,
        FakeUnitOfWork UnitOfWork,
        FakeMessageBus Bus
    );
}
