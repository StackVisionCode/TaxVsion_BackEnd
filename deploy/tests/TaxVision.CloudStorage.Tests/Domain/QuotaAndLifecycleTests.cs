using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Domain;

public sealed class QuotaAndLifecycleTests
{
    [Fact]
    public void Quota_counts_reserved_bytes_to_prevent_concurrent_overcommit()
    {
        var tenantId = Guid.NewGuid();
        var quota = TenantStorageLimit.Create(tenantId, "starter", maxBytes: 100, maxFileSizeBytes: 100);

        Assert.True(quota.Reserve(60).IsSuccess);
        var second = quota.Reserve(50);

        Assert.True(second.IsFailure);
        Assert.Equal(QuotaErrors.Exceeded, second.Error);
        Assert.Equal(60, quota.ReservedBytes);
        Assert.Equal(0, quota.UsedBytes);
    }

    [Fact]
    public void File_cannot_be_downloadable_before_a_clean_scan()
    {
        var tenantId = Guid.NewGuid();
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var result = FileObject.Register(
            Guid.NewGuid(),
            tenantId,
            OwnerType.Tenant,
            null,
            FolderType.Documents,
            2025,
            key,
            "return.pdf",
            "application/pdf",
            10,
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow.AddHours(24)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(FileStatus.PendingUpload, result.Value.Status);
        Assert.True(
            result
                .Value.MarkAvailable(
                    ChecksumSha256.Create(new string('a', 64)).Value,
                    "application/pdf",
                    DateTime.UtcNow
                )
                .IsFailure
        );
    }

    [Fact]
    public void Tax_folders_require_a_year()
    {
        var tenantId = Guid.NewGuid();
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/receipts/{Guid.NewGuid():N}.pdf").Value;

        var result = FileObject.Register(
            Guid.NewGuid(),
            tenantId,
            OwnerType.Tenant,
            null,
            FolderType.Receipts,
            null,
            key,
            "receipt.pdf",
            "application/pdf",
            10,
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow.AddHours(24)
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.YearRequired, result.Error);
    }

    [Fact]
    public void Pending_upload_can_only_expire_after_its_reservation_deadline()
    {
        var now = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/imports/{Guid.NewGuid():N}.csv").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Imports,
                null,
                key,
                "import.csv",
                "text/csv",
                10,
                Guid.NewGuid(),
                now,
                now.AddHours(24)
            )
            .Value;

        Assert.True(file.ExpireUpload(now.AddHours(23)).IsFailure);
        Assert.True(file.ExpireUpload(now.AddHours(25)).IsSuccess);
        Assert.Equal(FileStatus.ScanFailed, file.Status);
    }

    [Fact]
    public void Multipart_upload_id_can_only_be_attached_while_pending_upload()
    {
        var tenantId = Guid.NewGuid();
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
                "report.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;

        Assert.True(file.AttachMultipartUpload("upload-abc").IsSuccess);
        Assert.Equal("upload-abc", file.MultipartUploadId);

        file.MarkPendingScan(DateTime.UtcNow);
        Assert.True(file.AttachMultipartUpload("upload-def").IsFailure);
        Assert.Equal("upload-abc", file.MultipartUploadId); // no lo piso con el intento fallido
    }

    [Fact]
    public void Customer_portal_scope_only_accepts_its_own_customer_owner()
    {
        var customerId = Guid.NewGuid();
        var scope = new StorageActorScope(true, customerId);

        Assert.True(scope.CanCreate(OwnerType.Customer, customerId));
        Assert.False(scope.CanCreate(OwnerType.Customer, Guid.NewGuid()));
        Assert.False(scope.CanCreate(OwnerType.Tenant, null));
    }

    [Fact]
    public void MarkPendingScan_StampsScanQueuedAtUtc()
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var file = NewUploadedFile(now.AddMinutes(-5));

        Assert.True(file.MarkPendingScan(now).IsSuccess);

        Assert.Equal(now, file.ScanQueuedAtUtc);
        Assert.Equal(0, file.ScanAttemptCount);
    }

    [Fact]
    public void RequeueScan_FromPendingScan_CountsAttemptAndRestampsQueue()
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        var file = NewUploadedFile(now.AddHours(-3));
        file.MarkPendingScan(now.AddHours(-3));

        Assert.True(file.RequeueScan(now, maxAttempts: 3).IsSuccess);

        Assert.Equal(1, file.ScanAttemptCount);
        Assert.Equal(now, file.ScanQueuedAtUtc);
        Assert.Equal(FileStatus.PendingScan, file.Status);
    }

    [Fact]
    public void RequeueScan_FromAvailable_Fails()
    {
        var now = DateTime.UtcNow;
        var file = NewUploadedFile(now);
        file.MarkPendingScan(now);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", now);

        var result = file.RequeueScan(now, maxAttempts: 3);

        Assert.Equal(FileErrors.InvalidTransition, result.Error);
        Assert.Equal(0, file.ScanAttemptCount);
    }

    [Fact]
    public void RequeueScan_BeyondMaxAttempts_Fails()
    {
        var now = DateTime.UtcNow;
        var file = NewUploadedFile(now);
        file.MarkPendingScan(now);
        for (var i = 0; i < 3; i++)
            Assert.True(file.RequeueScan(now, maxAttempts: 3).IsSuccess);

        var result = file.RequeueScan(now.AddMinutes(1), maxAttempts: 3);

        Assert.Equal(FileErrors.ScanAttemptsExhausted, result.Error);
        Assert.Equal(3, file.ScanAttemptCount);
        Assert.Equal(now, file.ScanQueuedAtUtc);
    }

    [Fact]
    public void MarkScanAbandoned_FromPendingScan_MarksScanFailed()
    {
        var now = DateTime.UtcNow;
        var file = NewUploadedFile(now);
        file.MarkPendingScan(now);

        Assert.True(file.MarkScanAbandoned("blob_missing: gone", now, "blob_missing").IsSuccess);

        Assert.Equal(FileStatus.ScanFailed, file.Status);
        Assert.Equal("blob_missing: gone", file.ScanReport);
    }

    [Fact]
    public void RequestRescan_FromScanFailed_ReturnsToPendingScan()
    {
        var now = DateTime.UtcNow;
        var file = NewUploadedFile(now);
        file.MarkPendingScan(now);
        file.RequeueScan(now, maxAttempts: 3);
        file.MarkScanAbandoned("scan_stalled", now, "scan_stalled");

        Assert.True(file.RequestRescan(now.AddMinutes(1)).IsSuccess);

        Assert.Equal(FileStatus.PendingScan, file.Status);
        Assert.Equal(0, file.ScanAttemptCount);
        Assert.Equal(now.AddMinutes(1), file.ScanQueuedAtUtc);
    }

    [Fact]
    public void RequestRescan_FromAvailable_Fails()
    {
        var now = DateTime.UtcNow;
        var file = NewUploadedFile(now);
        file.MarkPendingScan(now);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", now);

        Assert.Equal(FileErrors.InvalidTransition, file.RequestRescan(now).Error);
    }

    [Fact]
    public void QuotaFootprint_SoftDeletedFromInfected_IsNone()
    {
        var now = DateTime.UtcNow;
        var file = NewUploadedFile(now);
        file.MarkPendingScan(now);
        file.MarkScanning();
        file.MarkInfected("Eicar", "application/pdf", now);
        file.SoftDelete(now, TimeSpan.FromDays(30));

        Assert.Equal(QuotaFootprint.None, file.QuotaFootprint);
    }

    [Fact]
    public void QuotaFootprint_SoftDeletedFromAvailable_IsUsed()
    {
        var now = DateTime.UtcNow;
        var file = NewAvailableFile(now);
        file.SoftDelete(now, TimeSpan.FromDays(30));

        Assert.Equal(QuotaFootprint.Used, file.QuotaFootprint);
    }

    [Fact]
    public void QuotaFootprint_SoftDeletedFromPendingUpload_IsReserved()
    {
        var now = DateTime.UtcNow;
        var file = NewUploadedFile(now);
        file.SoftDelete(now, TimeSpan.FromDays(30));

        Assert.Equal(QuotaFootprint.Reserved, file.QuotaFootprint);
    }

    [Fact]
    public void QuotaFootprint_DmcaBlocked_IsUsed()
    {
        var now = DateTime.UtcNow;
        var file = NewAvailableFile(now);
        file.BlockForTakedown(now);

        Assert.Equal(QuotaFootprint.Used, file.QuotaFootprint);
    }

    [Fact]
    public void QuotaFootprint_ModerationBlocked_IsNone()
    {
        var now = DateTime.UtcNow;
        var file = NewUploadedFile(now);
        file.MarkPendingScan(now);
        file.MarkScanning();
        file.MarkBlockedByPolicy("Policy violation.", "application/pdf", now);

        Assert.Equal(QuotaFootprint.None, file.QuotaFootprint);
    }

    [Fact]
    public void QuotaRelease_ReleasesUsedOrReservedByFootprint()
    {
        var now = DateTime.UtcNow;
        var available = NewAvailableFile(now);
        var pending = NewUploadedFile(now);
        var limit = TenantStorageLimit.Create(available.TenantId, "starter", maxBytes: 100, maxFileSizeBytes: 100);
        limit.Reserve(30);
        limit.Commit(10);

        QuotaRelease.Apply(limit, available);
        QuotaRelease.Apply(limit, pending);

        Assert.Equal(0, limit.UsedBytes);
        Assert.Equal(10, limit.ReservedBytes);
    }

    private static FileObject NewAvailableFile(DateTime now)
    {
        var file = NewUploadedFile(now);
        file.MarkPendingScan(now);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", now);
        return file;
    }

    private static FileObject NewUploadedFile(DateTime createdAtUtc)
    {
        var tenantId = Guid.NewGuid();
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
                "return.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                createdAtUtc,
                createdAtUtc.AddHours(24)
            )
            .Value;
    }
}
