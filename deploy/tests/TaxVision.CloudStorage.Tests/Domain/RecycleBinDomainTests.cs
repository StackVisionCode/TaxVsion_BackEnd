using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>Fase C1 — papelera real: FileObject.Restore y liberacion de cuota al purgar.</summary>
public sealed class RecycleBinDomainTests
{
    private static FileObject AvailableFile(Guid tenantId, long sizeBytes = 10)
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
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
        file.MarkPendingScan();
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", DateTime.UtcNow);
        return file;
    }

    [Fact]
    public void SoftDelete_then_Restore_returns_the_file_to_Available_and_clears_the_recycle_bin_timestamps()
    {
        var file = AvailableFile(Guid.NewGuid());
        file.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30));

        var result = file.Restore();

        Assert.True(result.IsSuccess);
        Assert.Equal(FileStatus.Available, file.Status);
        Assert.Null(file.SoftDeletedAtUtc);
        Assert.Null(file.SoftDeleteExpiresAtUtc);
    }

    [Fact]
    public void Restore_of_a_file_that_is_not_in_the_recycle_bin_fails()
    {
        var file = AvailableFile(Guid.NewGuid());

        var result = file.Restore();

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.InvalidTransition, result.Error);
    }

    [Fact]
    public void ReleaseUsed_decrements_UsedBytes_and_floors_at_zero()
    {
        var quota = TenantStorageLimit.Create(Guid.NewGuid(), "starter", maxBytes: 1000, maxFileSizeBytes: 1000);
        quota.Reserve(100);
        quota.Commit(100);
        Assert.Equal(100, quota.UsedBytes);

        quota.ReleaseUsed(40);
        Assert.Equal(60, quota.UsedBytes);

        quota.ReleaseUsed(1000);
        Assert.Equal(0, quota.UsedBytes);
    }
}
