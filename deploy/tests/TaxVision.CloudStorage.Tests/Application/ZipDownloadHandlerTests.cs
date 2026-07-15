using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fase B2 — PrepareZipDownloadHandler: valida y arma el plan de una descarga ZIP multi-archivo.</summary>
public sealed class ZipDownloadHandlerTests
{
    private static FileObject AvailableFile(Guid tenantId, string originalName = "report.pdf", long sizeBytes = 10)
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
                originalName,
                "application/pdf",
                sizeBytes,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
        file.MarkPendingScan();
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", DateTime.UtcNow);
        return file;
    }

    private static RequestAuditContext Audit() => new(null, null, "corr-1");

    private static IOptions<CloudStorageOptions> Options(int maxFiles = 500, long maxBytes = 500L * 1024 * 1024) =>
        Microsoft.Extensions.Options.Options.Create(
            new CloudStorageOptions { MaxZipFiles = maxFiles, MaxZipAggregateBytes = maxBytes }
        );

    [Fact]
    public async Task Two_available_files_produce_a_plan_and_audit_the_batch()
    {
        var tenantId = Guid.NewGuid();
        var fileA = AvailableFile(tenantId, "a.pdf", 100);
        var fileB = AvailableFile(tenantId, "b.pdf", 200);
        var files = new FakeFileObjectRepository();
        files.Seed(fileA);
        files.Seed(fileB);
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await PrepareZipDownloadHandler.Handle(
            new PrepareZipDownloadQuery(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                [fileA.Id, fileB.Id],
                Audit()
            ),
            files,
            audit,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Entries.Count);
        Assert.Equal(fileA.Id, result.Value.Entries[0].FileId);
        Assert.Equal(fileB.Id, result.Value.Entries[1].FileId);
        Assert.Single(audit.Logs, log => log.Action == "download.zip" && log.Details == "files=2;bytes=300");
        Assert.Equal(
            2,
            bus.Published.OfType<FileAccessAuditedIntegrationEvent>().Count(e => e.Action == "download.zip")
        );
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Duplicate_file_names_get_a_numeric_suffix_in_request_order()
    {
        var tenantId = Guid.NewGuid();
        var first = AvailableFile(tenantId, "report.pdf");
        var second = AvailableFile(tenantId, "report.pdf");
        var files = new FakeFileObjectRepository();
        files.Seed(first);
        files.Seed(second);

        var result = await PrepareZipDownloadHandler.Handle(
            new PrepareZipDownloadQuery(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                [first.Id, second.Id],
                Audit()
            ),
            files,
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("report.pdf", result.Value.Entries[0].EntryName);
        Assert.Equal("report_1.pdf", result.Value.Entries[1].EntryName);
    }

    [Fact]
    public async Task Empty_request_fails_without_auditing_or_saving()
    {
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await PrepareZipDownloadHandler.Handle(
            new PrepareZipDownloadQuery(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                [],
                Audit()
            ),
            new FakeFileObjectRepository(),
            audit,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NoFilesRequested, result.Error);
        Assert.Empty(audit.Logs);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Duplicate_ids_in_the_request_are_deduplicated_to_a_single_entry()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var bus = new FakeMessageBus();

        var result = await PrepareZipDownloadHandler.Handle(
            new PrepareZipDownloadQuery(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                [file.Id, file.Id],
                Audit()
            ),
            files,
            new FakeStorageAuditRepository(),
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Entries);
        Assert.Single(bus.Published.OfType<FileAccessAuditedIntegrationEvent>());
    }

    [Fact]
    public async Task Requesting_more_files_than_the_cap_fails_with_TooManyItems()
    {
        var tenantId = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var file = AvailableFile(tenantId, $"f{i}.pdf");
            files.Seed(file);
            ids.Add(file.Id);
        }
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await PrepareZipDownloadHandler.Handle(
            new PrepareZipDownloadQuery(tenantId, Guid.NewGuid(), new StorageActorScope(false, null), ids, Audit()),
            files,
            audit,
            Options(maxFiles: 2),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.TooManyItems, result.Error);
        Assert.Empty(audit.Logs);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_missing_or_cross_tenant_file_fails_the_whole_batch_with_NotFound()
    {
        var tenantId = Guid.NewGuid();
        var files = new FakeFileObjectRepository();
        var owned = AvailableFile(tenantId);
        files.Seed(owned);
        var otherTenantsFile = AvailableFile(Guid.NewGuid());
        files.Seed(otherTenantsFile);
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await PrepareZipDownloadHandler.Handle(
            new PrepareZipDownloadQuery(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                [owned.Id, otherTenantsFile.Id],
                Audit()
            ),
            files,
            audit,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
        Assert.Empty(audit.Logs);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_file_that_has_not_passed_the_scan_fails_the_whole_batch_with_NotAvailable()
    {
        var tenantId = Guid.NewGuid();
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var pendingFile = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "pending.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value; // se queda en PendingUpload, nunca escaneado
        var files = new FakeFileObjectRepository();
        files.Seed(pendingFile);
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await PrepareZipDownloadHandler.Handle(
            new PrepareZipDownloadQuery(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                [pendingFile.Id],
                Audit()
            ),
            files,
            audit,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotAvailable, result.Error);
        Assert.Empty(audit.Logs);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Aggregate_size_over_the_cap_fails_with_ZipTooLarge()
    {
        var tenantId = Guid.NewGuid();
        var fileA = AvailableFile(tenantId, "a.pdf", 300);
        var fileB = AvailableFile(tenantId, "b.pdf", 300);
        var files = new FakeFileObjectRepository();
        files.Seed(fileA);
        files.Seed(fileB);
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await PrepareZipDownloadHandler.Handle(
            new PrepareZipDownloadQuery(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                [fileA.Id, fileB.Id],
                Audit()
            ),
            files,
            audit,
            Options(maxBytes: 500),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.ZipTooLarge, result.Error);
        Assert.Empty(audit.Logs);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
