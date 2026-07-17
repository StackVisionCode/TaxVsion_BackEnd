using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.RecycleBin;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fase C1 — RestoreFileHandler, EmptyRecycleBinHandler y GetRecycleBinHandler.</summary>
public sealed class RecycleBinHandlerTests
{
    private static FileObject SoftDeletedFile(Guid tenantId, DateTime nowUtc, long sizeBytes = 10)
    {
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "return.pdf",
                "application/pdf",
                sizeBytes,
                Guid.NewGuid(),
                nowUtc,
                nowUtc.AddHours(24)
            )
            .Value;
        file.MarkPendingScan();
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", nowUtc);
        file.SoftDelete(nowUtc, TimeSpan.FromDays(30));
        return file;
    }

    private static RequestAuditContext Audit() => new(null, null, "corr-1");

    [Fact]
    public async Task Restore_of_a_soft_deleted_file_succeeds_and_publishes_the_integration_event()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var file = SoftDeletedFile(tenantId, now);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var audit = new FakeStorageAuditRepository();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await RestoreFileHandler.Handle(
            new RestoreFileCommand(tenantId, Guid.NewGuid(), file.Id, Audit()),
            files,
            audit,
            new FakeSystemClock(now),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(FileStatus.Available, file.Status);
        Assert.Single(audit.Logs, log => log.Action == "restore" && log.Outcome == "success");
        Assert.Single(
            bus.Published.OfType<BuildingBlocks.Messaging.CloudStorageIntegrationEvents.FileRestoredIntegrationEvent>()
        );
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Restore_of_a_file_belonging_to_another_tenant_is_not_found()
    {
        var file = SoftDeletedFile(Guid.NewGuid(), DateTime.UtcNow);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var result = await RestoreFileHandler.Handle(
            new RestoreFileCommand(Guid.NewGuid(), Guid.NewGuid(), file.Id, Audit()),
            files,
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Restore_of_a_file_that_is_not_in_the_recycle_bin_fails_without_publishing_anything()
    {
        var tenantId = Guid.NewGuid();
        var file = SoftDeletedFile(tenantId, DateTime.UtcNow);
        file.Restore(); // ya no esta en la papelera
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var bus = new FakeMessageBus();

        var result = await RestoreFileHandler.Handle(
            new RestoreFileCommand(tenantId, Guid.NewGuid(), file.Id, Audit()),
            files,
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task EmptyRecycleBin_purges_every_soft_deleted_file_and_releases_quota()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var fileA = SoftDeletedFile(tenantId, now, sizeBytes: 30);
        var fileB = SoftDeletedFile(tenantId, now, sizeBytes: 20);
        var otherTenantFile = SoftDeletedFile(Guid.NewGuid(), now, sizeBytes: 999);
        var files = new FakeFileObjectRepository();
        files.Seed(fileA);
        files.Seed(fileB);
        files.Seed(otherTenantFile);

        var limit = TenantStorageLimit.Create(tenantId, "starter", maxBytes: 1000, maxFileSizeBytes: 1000);
        limit.Reserve(50);
        limit.Commit(50); // UsedBytes=50, coherente con fileA(30)+fileB(20) ya "contando" en la papelera
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);

        var audit = new FakeStorageAuditRepository();
        var storage = new FakeObjectStorage();
        var unitOfWork = new FakeUnitOfWork();
        var options = Options.Create(new CloudStorageOptions());

        var purgedCount = await EmptyRecycleBinHandler.Handle(
            new EmptyRecycleBinCommand(tenantId, Guid.NewGuid(), Audit()),
            files,
            limits,
            audit,
            storage,
            options,
            new FakeSystemClock(now),
            unitOfWork,
            CancellationToken.None
        );

        Assert.Equal(2, purgedCount);
        Assert.True(files.Removed(fileA.Id));
        Assert.True(files.Removed(fileB.Id));
        Assert.False(files.Removed(otherTenantFile.Id)); // otro tenant, intacto
        Assert.Equal(0, limit.UsedBytes);
        Assert.Equal(2, storage.Deleted.Count);
        Assert.Equal(2, audit.Logs.Count(log => log.Action == "delete.purge"));
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task EmptyRecycleBin_skips_legal_held_files_and_does_not_release_their_quota_or_delete_them()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var purgeable = SoftDeletedFile(tenantId, now, sizeBytes: 30);
        var held = SoftDeletedFile(tenantId, now, sizeBytes: 20);
        held.PlaceLegalHold();
        var files = new FakeFileObjectRepository();
        files.Seed(purgeable);
        files.Seed(held);

        var limit = TenantStorageLimit.Create(tenantId, "starter", maxBytes: 1000, maxFileSizeBytes: 1000);
        limit.Reserve(50);
        limit.Commit(50); // UsedBytes=50, coherente con purgeable(30)+held(20)
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);

        var audit = new FakeStorageAuditRepository();
        var storage = new FakeObjectStorage();
        var unitOfWork = new FakeUnitOfWork();
        var options = Options.Create(new CloudStorageOptions());

        var purgedCount = await EmptyRecycleBinHandler.Handle(
            new EmptyRecycleBinCommand(tenantId, Guid.NewGuid(), Audit()),
            files,
            limits,
            audit,
            storage,
            options,
            new FakeSystemClock(now),
            unitOfWork,
            CancellationToken.None
        );

        Assert.Equal(1, purgedCount);
        Assert.True(files.Removed(purgeable.Id));
        Assert.False(files.Removed(held.Id));
        Assert.Equal(20, limit.UsedBytes); // solo se libero la cuota del archivo purgado
        Assert.Single(storage.Deleted);
        Assert.Single(audit.Logs, log => log.Action == "delete.purge-skipped" && log.Outcome == "blocked");
    }

    [Fact]
    public async Task GetRecycleBin_only_lists_the_caller_tenant_s_soft_deleted_files()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var ownFile = SoftDeletedFile(tenantId, now);
        var otherTenantFile = SoftDeletedFile(Guid.NewGuid(), now);
        var files = new FakeFileObjectRepository();
        files.Seed(ownFile);
        files.Seed(otherTenantFile);

        var result = await GetRecycleBinHandler.Handle(
            new GetRecycleBinQuery(tenantId, 0, 50),
            files,
            CancellationToken.None
        );

        var item = Assert.Single(result);
        Assert.Equal(ownFile.Id, item.Id);
    }
}
