using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class RescanFileHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_ScanFailedWithBlob_ReservesAndEnqueues()
    {
        var fixture = CreateFixture(maxBytes: 1_000);
        fixture.Storage.Existing.Add((fixture.Options.TempBucket, fixture.File.ObjectKey));

        var result = await RescanAsync(fixture);

        Assert.True(result.IsSuccess);
        Assert.Equal(FileStatus.PendingScan, fixture.File.Status);
        Assert.Equal(Now, fixture.File.ScanQueuedAtUtc);
        Assert.Equal(fixture.File.SizeBytes, fixture.Limit.ReservedBytes);
        var queued = Assert.Single(fixture.Bus.Published.OfType<ScanFileCommand>());
        Assert.Equal(fixture.File.Id, queued.FileId);
        Assert.Equal(1, fixture.UnitOfWork.SaveChangesCallCount);
        Assert.Equal("scan.rescan", Assert.Single(fixture.Audit.Logs).Action);
    }

    [Fact]
    public async Task Handle_QuotaFull_ReturnsConflictWithoutChanges()
    {
        var fixture = CreateFixture(maxBytes: 50); // el archivo pesa 40, ya hay 20 usados
        Assert.True(fixture.Limit.Reserve(20).IsSuccess);
        fixture.Limit.Commit(20);
        fixture.Storage.Existing.Add((fixture.Options.MainBucket, fixture.File.ObjectKey));

        var result = await RescanAsync(fixture);

        Assert.Equal(QuotaErrors.Exceeded, result.Error);
        Assert.Equal(FileStatus.ScanFailed, fixture.File.Status);
        Assert.Equal(0, fixture.Limit.ReservedBytes);
        Assert.Empty(fixture.Bus.Published);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_BlobMissing_ReturnsBlobMissing()
    {
        var fixture = CreateFixture(maxBytes: 1_000);

        var result = await RescanAsync(fixture);

        Assert.Equal(FileErrors.BlobMissing, result.Error);
        Assert.Equal(FileStatus.ScanFailed, fixture.File.Status);
        Assert.Equal(0, fixture.Limit.ReservedBytes);
        Assert.Empty(fixture.Bus.Published);
        Assert.Equal(0, fixture.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_FileNotScanFailed_ReturnsInvalidTransitionWithoutChanges()
    {
        var fixture = CreateFixture(maxBytes: 1_000);
        Assert.True(fixture.File.RequestRescan(Now.AddMinutes(-1)).IsSuccess);
        fixture.Storage.Existing.Add((fixture.Options.TempBucket, fixture.File.ObjectKey));

        var result = await RescanAsync(fixture);

        Assert.Equal(FileErrors.InvalidTransition, result.Error);
        Assert.Empty(fixture.Bus.Published);
        Assert.Equal(0, fixture.Limit.ReservedBytes);
    }

    private static RescanFixture CreateFixture(long maxBytes)
    {
        var tenantId = Guid.NewGuid();
        var createdAt = Now.AddDays(-1);
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
                "failed.pdf",
                "application/pdf",
                40,
                Guid.NewGuid(),
                createdAt,
                createdAt.AddHours(1)
            )
            .Value;
        Assert.True(file.MarkPendingScan(createdAt).IsSuccess);
        Assert.True(file.MarkScanning().IsSuccess);
        Assert.True(file.MarkScanFailed("Unexpected error: SocketException: refused", createdAt).IsSuccess);

        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var limit = TenantStorageLimit.Create(tenantId, "starter", maxBytes, maxBytes);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);

        return new RescanFixture(
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

    private static Task<Result> RescanAsync(RescanFixture fixture) =>
        RescanFileHandler.Handle(
            new RescanFileCommand(fixture.File.TenantId, fixture.File.Id, Guid.NewGuid()),
            fixture.Files,
            fixture.Limits,
            fixture.Audit,
            fixture.Storage,
            Options.Create(fixture.Options),
            new FakeSystemClock(Now),
            fixture.UnitOfWork,
            fixture.Bus,
            CancellationToken.None
        );

    private sealed record RescanFixture(
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
