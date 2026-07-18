using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fase U — InitiateMultipartUploadHandler y CompleteMultipartUploadHandler.</summary>
public sealed class MultipartUploadHandlerTests
{
    private static RequestAuditContext Audit() => new(null, null, "corr-1");

    private static IOptions<CloudStorageOptions> Options(long partSizeBytes = 5L * 1024 * 1024) =>
        Microsoft.Extensions.Options.Options.Create(new CloudStorageOptions { MultipartPartSizeBytes = partSizeBytes });

    private static InitiateMultipartUploadRequest Request(long sizeBytes) =>
        new("large-report.pdf", "application/pdf", sizeBytes, OwnerType.Tenant, null, FolderType.Documents, 2025);

    [Fact]
    public async Task Valid_request_computes_part_count_and_returns_one_url_per_part()
    {
        var tenantId = Guid.NewGuid();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(
            TenantStorageLimit.Create(tenantId, "starter", maxBytes: 100_000_000, maxFileSizeBytes: 100_000_000)
        );
        var files = new FakeFileObjectRepository();
        var audit = new FakeStorageAuditRepository();
        var multipart = new FakeMultipartUploadStorage();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await InitiateMultipartUploadHandler.Handle(
            new InitiateMultipartUploadCommand(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                Request(sizeBytes: 12L * 1024 * 1024), // 12MB / 5MB parts = 3 partes
                Audit()
            ),
            files,
            limits,
            audit,
            new DefaultObjectKeyBuilder(),
            multipart,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Parts.Count);
        Assert.Equal([1, 2, 3], result.Value.Parts.Select(p => p.PartNumber));
        Assert.Single(
            multipart.Initiated,
            i => i.TotalSizeBytes == 12L * 1024 * 1024 && i.PartSizeBytes == 5L * 1024 * 1024
        );

        var registered = await files.GetAsync(tenantId, result.Value.FileId, CancellationToken.None);
        Assert.NotNull(registered);
        Assert.Equal(FileStatus.PendingUpload, registered!.Status);
        Assert.Single(
            audit.Logs,
            log => log.Action == "upload.multipart-initiated" && log.Details == "size=12582912;parts=3"
        );
        Assert.Single(
            bus.Published.OfType<FileAccessAuditedIntegrationEvent>(),
            e => e.Action == "upload.multipart-initiated"
        );
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Missing_quota_provisioning_fails_without_calling_storage()
    {
        var multipart = new FakeMultipartUploadStorage();

        var result = await InitiateMultipartUploadHandler.Handle(
            new InitiateMultipartUploadCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                Request(sizeBytes: 12L * 1024 * 1024),
                Audit()
            ),
            new FakeFileObjectRepository(),
            new FakeStorageLimitRepository(), // sin seed
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            multipart,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(QuotaErrors.NotProvisioned, result.Error);
        Assert.Empty(multipart.Initiated);
    }

    [Fact]
    public async Task Extension_rejected_by_the_upload_policy_fails_without_calling_storage()
    {
        var tenantId = Guid.NewGuid();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(
            TenantStorageLimit.Create(tenantId, "starter", maxBytes: 100_000_000, maxFileSizeBytes: 100_000_000)
        );
        var multipart = new FakeMultipartUploadStorage();

        var result = await InitiateMultipartUploadHandler.Handle(
            new InitiateMultipartUploadCommand(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                new InitiateMultipartUploadRequest(
                    "malware.exe",
                    "application/octet-stream",
                    12L * 1024 * 1024,
                    OwnerType.Tenant,
                    null,
                    FolderType.Documents,
                    2025
                ),
                Audit()
            ),
            new FakeFileObjectRepository(),
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            multipart,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.UnsupportedType, result.Error);
        Assert.Empty(multipart.Initiated);
    }

    [Fact]
    public async Task Quota_exceeded_publishes_the_limit_event_and_fails()
    {
        var tenantId = Guid.NewGuid();
        var limits = new FakeStorageLimitRepository();
        limits.Seed(TenantStorageLimit.Create(tenantId, "starter", maxBytes: 100, maxFileSizeBytes: 100_000_000));
        var bus = new FakeMessageBus();
        var multipart = new FakeMultipartUploadStorage();

        var result = await InitiateMultipartUploadHandler.Handle(
            new InitiateMultipartUploadCommand(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                Request(sizeBytes: 12L * 1024 * 1024),
                Audit()
            ),
            new FakeFileObjectRepository(),
            limits,
            new FakeStorageAuditRepository(),
            new DefaultObjectKeyBuilder(),
            multipart,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(QuotaErrors.Exceeded, result.Error);
        Assert.Single(bus.Published.OfType<StorageLimitExceededIntegrationEvent>());
        Assert.Empty(multipart.Initiated);
    }

    private static FileObject PendingUploadFile(Guid tenantId, long sizeBytes)
    {
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "large-report.pdf",
                "application/pdf",
                sizeBytes,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
    }

    [Fact]
    public async Task Complete_assembles_the_parts_then_marks_the_file_pending_scan()
    {
        var tenantId = Guid.NewGuid();
        var file = PendingUploadFile(tenantId, sizeBytes: 12L * 1024 * 1024);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var limits = new FakeStorageLimitRepository();
        var audit = new FakeStorageAuditRepository();
        var storage = new FakeObjectStorage { SizeToReturn = file.SizeBytes };
        var multipart = new FakeMultipartUploadStorage();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CompleteMultipartUploadHandler.Handle(
            new CompleteMultipartUploadCommand(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                file.Id,
                new CompleteMultipartUploadRequest(
                    "upload-abc",
                    [new MultipartPartCompletion(1, "etag-1"), new MultipartPartCompletion(2, "etag-2")]
                ),
                Audit()
            ),
            files,
            limits,
            audit,
            storage,
            multipart,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(FileStatus.PendingScan, file.Status);
        Assert.Single(
            multipart.Completed,
            c => c.UploadId == "upload-abc" && c.ObjectKey == file.ObjectKey && c.Parts.Count == 2
        );
        Assert.Single(bus.Published.OfType<ScanFileCommand>(), cmd => cmd.FileId == file.Id);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Complete_on_a_missing_file_fails_without_touching_storage()
    {
        var multipart = new FakeMultipartUploadStorage();

        var result = await CompleteMultipartUploadHandler.Handle(
            new CompleteMultipartUploadCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                Guid.NewGuid(),
                new CompleteMultipartUploadRequest("upload-abc", [new MultipartPartCompletion(1, "etag-1")]),
                Audit()
            ),
            new FakeFileObjectRepository(),
            new FakeStorageLimitRepository(),
            new FakeStorageAuditRepository(),
            new FakeObjectStorage(),
            multipart,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
        Assert.Empty(multipart.Completed);
    }

    [Fact]
    public async Task Complete_on_a_file_not_pending_upload_fails_without_touching_storage()
    {
        var tenantId = Guid.NewGuid();
        var file = PendingUploadFile(tenantId, sizeBytes: 12L * 1024 * 1024);
        file.MarkPendingScan(); // ya no esta en PendingUpload
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var multipart = new FakeMultipartUploadStorage();

        var result = await CompleteMultipartUploadHandler.Handle(
            new CompleteMultipartUploadCommand(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                file.Id,
                new CompleteMultipartUploadRequest("upload-abc", [new MultipartPartCompletion(1, "etag-1")]),
                Audit()
            ),
            files,
            new FakeStorageLimitRepository(),
            new FakeStorageAuditRepository(),
            new FakeObjectStorage(),
            multipart,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.InvalidTransition, result.Error);
        Assert.Empty(multipart.Completed);
    }

    [Fact]
    public async Task Complete_with_a_size_mismatch_rejects_the_upload_and_releases_quota()
    {
        var tenantId = Guid.NewGuid();
        var file = PendingUploadFile(tenantId, sizeBytes: 12L * 1024 * 1024);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var limit = TenantStorageLimit.Create(
            tenantId,
            "starter",
            maxBytes: 100_000_000,
            maxFileSizeBytes: 100_000_000
        );
        limit.Reserve(file.SizeBytes);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);
        var storage = new FakeObjectStorage { SizeToReturn = file.SizeBytes - 1 }; // no matchea lo declarado
        var multipart = new FakeMultipartUploadStorage();

        var result = await CompleteMultipartUploadHandler.Handle(
            new CompleteMultipartUploadCommand(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                file.Id,
                new CompleteMultipartUploadRequest("upload-abc", [new MultipartPartCompletion(1, "etag-1")]),
                Audit()
            ),
            files,
            limits,
            new FakeStorageAuditRepository(),
            storage,
            multipart,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.UploadSizeMismatch, result.Error);
        Assert.Equal(FileStatus.ScanFailed, file.Status);
        // El assemble en MinIO SI se llego a llamar (paso previo al chequeo de tamano) — el
        // objeto assemblado queda huerfano en TempBucket hasta el TTL de 24h (ver
        // MinioBucketBootstrapper), consistente con como ya se maneja el mismatch del flujo
        // de un solo POST.
        Assert.Single(multipart.Completed);
    }

    [Fact]
    public async Task Complete_aborts_the_multipart_upload_when_assembly_fails_in_the_storage()
    {
        var tenantId = Guid.NewGuid();
        var file = PendingUploadFile(tenantId, sizeBytes: 12L * 1024 * 1024);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var multipart = new FakeMultipartUploadStorage { ThrowOnComplete = true };
        var unitOfWork = new FakeUnitOfWork();

        var result = await CompleteMultipartUploadHandler.Handle(
            new CompleteMultipartUploadCommand(
                tenantId,
                Guid.NewGuid(),
                new StorageActorScope(false, null),
                file.Id,
                new CompleteMultipartUploadRequest(
                    "upload-abc",
                    [new MultipartPartCompletion(1, "etag-1"), new MultipartPartCompletion(2, "etag-2")]
                ),
                Audit()
            ),
            files,
            new FakeStorageLimitRepository(),
            new FakeStorageAuditRepository(),
            new FakeObjectStorage(),
            multipart,
            Options(),
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.MultipartCompleteFailed, result.Error);
        Assert.Empty(multipart.Completed);
        Assert.Single(multipart.Aborted, a => a.UploadId == "upload-abc" && a.ObjectKey == file.ObjectKey);
        // El archivo se queda en PendingUpload — ExpiredUploadCleanupService lo termina de
        // limpiar (ExpireUpload) cuando venza la reserva, sin necesidad de que este handler
        // haga nada mas aca.
        Assert.Equal(FileStatus.PendingUpload, file.Status);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
