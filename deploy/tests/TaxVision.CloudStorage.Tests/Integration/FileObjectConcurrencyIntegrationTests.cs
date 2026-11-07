using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Infrastructure.Storage;
using TaxVision.CloudStorage.Infrastructure.Time;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>Dos escrituras sobre la misma fila de Files, contra SQL real: gana la primera y la otra se entera.</summary>
[Collection(CloudStorageConcurrencyCollection.Name)]
public sealed class FileObjectConcurrencyIntegrationTests(CloudStorageConcurrencyFixture fixture)
{
    private const string Sha256 = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const long FileSize = 4;
    private static readonly TimeSpan OverlapWait = TimeSpan.FromSeconds(3);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly CloudStorageOptions _options = new();

    [Fact]
    public async Task ScanCompletesAfterSoftDelete_SecondSaveConflicts()
    {
        var fileId = await SeedFileAsync(scanning: true);
        await using var scan = fixture.NewContext(_tenantId);
        await using var delete = fixture.NewContext(_tenantId);
        var scanning = await scan.Files.SingleAsync(f => f.Id == fileId);
        var deleted = await delete.Files.SingleAsync(f => f.Id == fileId);

        Assert.True(deleted.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);
        await delete.SaveChangesAsync();
        Assert.True(
            scanning.MarkAvailable(ChecksumSha256.Create(Sha256).Value, "application/pdf", DateTime.UtcNow).IsSuccess
        );

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => scan.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        Assert.Equal(FileStatus.SoftDeleted, (await ReadFileAsync(fileId)).Status);
    }

    [Fact]
    public async Task A_file_and_the_quota_row_conflicting_together_is_a_conflict_and_saves_nothing()
    {
        var fileId = await SeedFileAsync(scanning: true);
        await using var winner = fixture.NewContext(_tenantId);
        await using var loser = fixture.NewContext(_tenantId);
        var winnerFile = await winner.Files.SingleAsync(f => f.Id == fileId);
        var winnerLimit = await winner.StorageLimits.SingleAsync();
        var loserFile = await loser.Files.SingleAsync(f => f.Id == fileId);
        var loserLimit = await loser.StorageLimits.SingleAsync();

        var checksum = ChecksumSha256.Create(Sha256).Value;
        Assert.True(winnerFile.MarkAvailable(checksum, "application/pdf", DateTime.UtcNow).IsSuccess);
        winnerLimit.Commit(FileSize);
        await winner.SaveChangesAsync();

        Assert.True(loserFile.MarkAvailable(checksum, "application/pdf", DateTime.UtcNow).IsSuccess);
        loserLimit.Commit(FileSize);
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => loser.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
        Assert.Equal(FileSize, (await ReadLimitAsync()).UsedBytes);
    }

    [Fact]
    public async Task DuplicateScanInParallel_CommitsQuotaOnce()
    {
        var fileId = await SeedFileAsync(scanning: false);
        var storage = new FakeObjectStorage { DownloadContent = [0x25, 0x50, 0x44, 0x46] };
        storage.Existing.Add((_options.TempBucket, (await ReadFileAsync(fileId)).ObjectKey));
        var scanner = new OverlappingVirusScanner();

        await Task.WhenAll(ScanAsync(fileId, storage, scanner), ScanAsync(fileId, storage, scanner));

        Assert.Equal(FileStatus.Available, (await ReadFileAsync(fileId)).Status);
        var limit = await ReadLimitAsync();
        Assert.Equal(FileSize, limit.UsedBytes);
        Assert.Equal(0, limit.ReservedBytes);
        Assert.Equal(1, scanner.Scans);
    }

    [Fact]
    public async Task A_new_file_whose_quota_row_changed_merges_the_counter_and_is_inserted()
    {
        var existing = await SeedFileAsync(scanning: true);
        await using var loser = fixture.NewContext(_tenantId);
        await using var winner = fixture.NewContext(_tenantId);
        var loserLimit = await loser.StorageLimits.SingleAsync();
        var winnerLimit = await winner.StorageLimits.SingleAsync();

        winnerLimit.Commit(FileSize);
        await winner.SaveChangesAsync();

        Assert.True(loserLimit.Reserve(10).IsSuccess);
        var file = NewFile(Guid.NewGuid(), "second.pdf", 10);
        loser.Files.Add(file);
        await loser.SaveChangesAsync();

        await using var db = fixture.NewContext(_tenantId);
        Assert.Equal(2, await db.Files.CountAsync());
        Assert.NotEmpty((await ReadFileAsync(file.Id)).RowVersion);
        Assert.Equal(10, (await ReadLimitAsync()).ReservedBytes);
        Assert.Equal(FileStatus.Scanning, (await ReadFileAsync(existing)).Status);
    }

    [Fact]
    public async Task A_save_request_that_loses_the_quota_race_is_rejected_and_cleans_the_source()
    {
        const long max = 1_000;
        var fileId = Guid.NewGuid();
        await using (var seed = fixture.NewContext(_tenantId))
        {
            seed.StorageLimits.Add(TenantStorageLimit.Create(_tenantId, "starter", max, max));
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.NewContext(_tenantId);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var storage = new FakeObjectStorage { SizeToReturn = 600 };
        var bus = new FakeMessageBus();

        await SaveFileFromSourceHandler.Handle(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = _tenantId,
                FileId = fileId,
                RequestingService = "documents",
                SourceBucket = "taxvision-temp",
                SourceObjectKey = $"documents/{fileId:N}/race.pdf",
                ActorId = Guid.NewGuid(),
                OwnerType = "Tenant",
                OwnerId = null,
                FolderType = "Documents",
                TaxYear = 2026,
                OriginalName = "race.pdf",
                ContentType = "application/pdf",
                SizeBytes = 600,
                CorrelationId = "it-quota-race",
            },
            new FileObjectRepository(db),
            new StorageLimitRepository(db),
            new StorageAuditRepository(db),
            new DefaultObjectKeyBuilder(),
            storage,
            new SystemFolderProvisioner(new FolderRepository(db)),
            Options.Create(_options),
            // La reserva rival entra justo después de que el handler leyó la fila de cuota.
            new RacingPolicyResolver(() => ReserveInAnotherContextAsync(600)),
            new SystemClock(),
            db,
            bus,
            new FixedCorrelation(),
            NullLogger<SaveFileRequestedIntegrationEvent>.Instance,
            CancellationToken.None
        );
        // Wolverine guarda lo pendiente antes del commit: tiene que estar limpio.
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        Assert.Equal("QuotaExceeded", Assert.Single(bus.Published.OfType<FileSaveRejectedIntegrationEvent>()).Reason);
        Assert.Empty(bus.Published.OfType<ScanFileCommand>());
        Assert.Empty(storage.Copied);
        Assert.Equal(("taxvision-temp", $"documents/{fileId:N}/race.pdf"), Assert.Single(storage.Deleted));
        await using var check = fixture.NewContext(_tenantId);
        Assert.False(await check.Files.AnyAsync(f => f.Id == fileId));
        Assert.Equal(600, (await ReadLimitAsync()).ReservedBytes);
        var log = await check.AccessLogs.AsNoTracking().SingleAsync(l => l.FileId == fileId);
        Assert.Equal("rejected", log.Outcome);
    }

    private async Task ReserveInAnotherContextAsync(long bytes)
    {
        await using var rival = fixture.NewContext(_tenantId);
        var limit = await rival.StorageLimits.SingleAsync();
        Assert.True(limit.Reserve(bytes).IsSuccess);
        await rival.SaveChangesAsync();
    }

    // Como en Wolverine: el escaneo sin transacción y el veredicto en la suya, en otro contexto.
    private async Task ScanAsync(Guid fileId, FakeObjectStorage storage, IVirusScanner scanner)
    {
        await using var db = fixture.NewContext(_tenantId);
        var bus = new FakeMessageBus { OnInvoke = message => RecordVerdictAsync((RecordScanVerdictCommand)message, storage) };
        await ScanFileHandler.Handle(
            new ScanFileCommand(_tenantId, fileId, "it-duplicate-scan"),
            new SqlScanClaims(db),
            new FileObjectRepository(db),
            new StorageLimitRepository(db),
            storage,
            scanner,
            new CleanContentScanner(),
            new PdfInspector(),
            Options.Create(_options),
            new FakeUploadPolicyResolver(),
            db,
            bus,
            NullLogger<ScanFileCommand>.Instance,
            CancellationToken.None
        );
    }

    private async Task RecordVerdictAsync(RecordScanVerdictCommand command, FakeObjectStorage storage)
    {
        await using var db = fixture.NewContext(_tenantId);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await RecordScanVerdictHandler.Handle(
            command,
            new FileObjectRepository(db),
            new StorageLimitRepository(db),
            new StorageAuditRepository(db),
            storage,
            Options.Create(_options),
            new SystemClock(),
            db,
            new FakeMessageBus(),
            CancellationToken.None
        );
        await transaction.CommitAsync();
    }

    private FileObject NewFile(Guid fileId, string name, long size)
    {
        var now = DateTime.UtcNow;
        var key = ObjectKey.Create($"tenants/{_tenantId:N}/tenant/documents/2026/{fileId:N}.pdf").Value;
        return FileObject
            .Register(
                fileId,
                _tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                name,
                "application/pdf",
                size,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
    }

    private async Task<Guid> SeedFileAsync(bool scanning)
    {
        var fileId = Guid.NewGuid();
        var file = NewFile(fileId, "concurrency.pdf", FileSize);
        Assert.True(file.MarkPendingScan(DateTime.UtcNow).IsSuccess);
        if (scanning)
            Assert.True(file.MarkScanning().IsSuccess);

        var limit = TenantStorageLimit.Create(_tenantId, "starter", 100_000_000, 100_000_000);
        Assert.True(limit.Reserve(FileSize).IsSuccess);

        await using var db = fixture.NewContext(_tenantId);
        db.StorageLimits.Add(limit);
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return fileId;
    }

    private async Task<FileObject> ReadFileAsync(Guid fileId)
    {
        await using var db = fixture.NewContext(_tenantId);
        return await db.Files.AsNoTracking().SingleAsync(f => f.Id == fileId);
    }

    private async Task<TenantStorageLimit> ReadLimitAsync()
    {
        await using var db = fixture.NewContext(_tenantId);
        return await db.StorageLimits.AsNoTracking().SingleAsync();
    }

    /// <summary>Retiene el primer escaneo hasta que llegue el segundo, para que se solapen de verdad.</summary>
    private sealed class OverlappingVirusScanner : IVirusScanner
    {
        private readonly TaskCompletionSource _second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _scans;

        public int Scans => _scans;

        public async Task<VirusScanResult> ScanAsync(Stream content, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _scans) > 1)
                _second.TrySetResult();
            else
                await Task.WhenAny(_second.Task, Task.Delay(OverlapWait, ct));
            return VirusScanResult.Clean();
        }
    }

    private sealed class RacingPolicyResolver(Func<Task> race) : IUploadPolicyResolver
    {
        private readonly FakeUploadPolicyResolver _inner = new();

        public async Task<EffectiveUploadPolicy> ResolveAsync(
            Guid tenantId,
            TenantStorageLimit limit,
            FolderType folderType,
            bool forCustomerPortal,
            CancellationToken ct
        )
        {
            await race();
            return await _inner.ResolveAsync(tenantId, limit, folderType, forCustomerPortal, ct);
        }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public string CorrelationId => "it-quota-race";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class CleanContentScanner : IContentScanner
    {
        public Task<ContentScanResult> ScanAsync(Stream content, ContentScanContext context, CancellationToken ct) =>
            Task.FromResult(ContentScanResult.Clean());
    }

    private sealed class PdfInspector : IFileContentInspector
    {
        public Task<InspectedContent> InspectAsync(Stream content, string originalName, CancellationToken ct) =>
            Task.FromResult(new InspectedContent("application/pdf", Sha256));
    }
}
