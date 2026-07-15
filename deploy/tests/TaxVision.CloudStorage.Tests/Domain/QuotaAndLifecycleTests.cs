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

        file.MarkPendingScan();
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
}
