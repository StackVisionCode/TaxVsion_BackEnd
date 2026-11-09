using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Domain;

public sealed class TenantStorageLimitTests
{
    private const long MB = 1024L * 1024;

    [Fact]
    public void Reserve_no_longer_caps_by_the_plan_generic_per_file_limit()
    {
        // D-33: el tope por archivo lo decide UploadSizePolicy (2 GB para Recordings) antes de reservar;
        // si el agregado siguiera comparando contra los 25 MB genéricos del plan, el override quedaría muerto.
        var limit = TenantStorageLimit.Create(
            Guid.NewGuid(),
            "enterprise",
            maxBytes: 10L * 1024 * MB,
            maxFileSizeBytes: 25 * MB
        );

        var result = limit.Reserve(2048 * MB);

        Assert.True(result.IsSuccess);
        Assert.Equal(2048 * MB, limit.ReservedBytes);
    }

    [Fact]
    public void Reserve_still_enforces_the_tenant_quota_and_rejects_empty_files()
    {
        var limit = TenantStorageLimit.Create(Guid.NewGuid(), "starter", maxBytes: 100 * MB, maxFileSizeBytes: 10 * MB);

        Assert.Equal(QuotaErrors.Exceeded, limit.Reserve(101 * MB).Error);
        Assert.Equal(QuotaErrors.FileTooLarge, limit.Reserve(0).Error);
        Assert.Equal(0, limit.ReservedBytes);
    }

    [Fact]
    public void Reserve_is_rejected_while_suspended()
    {
        var limit = TenantStorageLimit.Create(Guid.NewGuid(), "starter", maxBytes: 100 * MB, maxFileSizeBytes: 10 * MB);
        limit.Suspend();

        Assert.Equal(QuotaErrors.Suspended, limit.Reserve(1 * MB).Error);
    }

    [Fact]
    public void Recalculate_SetsAbsoluteCountersAndIsIdempotent()
    {
        var limit = TenantStorageLimit.Create(Guid.NewGuid(), "starter", maxBytes: 100 * MB, maxFileSizeBytes: 10 * MB);
        limit.Reserve(40 * MB);
        limit.Commit(30 * MB);

        Assert.True(limit.Recalculate(usedBytes: 5 * MB, reservedBytes: 2 * MB).IsSuccess);
        Assert.True(limit.Recalculate(usedBytes: 5 * MB, reservedBytes: 2 * MB).IsSuccess);

        Assert.Equal(5 * MB, limit.UsedBytes);
        Assert.Equal(2 * MB, limit.ReservedBytes);
    }

    [Fact]
    public void Recalculate_NegativeCounters_Fails()
    {
        var limit = TenantStorageLimit.Create(Guid.NewGuid(), "starter", maxBytes: 100 * MB, maxFileSizeBytes: 10 * MB);

        Assert.True(limit.Recalculate(usedBytes: -1, reservedBytes: 0).IsFailure);
        Assert.Equal(0, limit.UsedBytes);
    }

    [Fact]
    public void ApplyEntitlements_SameRevision_IsNoOp()
    {
        var limit = TenantStorageLimit.Create(Guid.NewGuid(), "starter", maxBytes: 100 * MB, maxFileSizeBytes: 10 * MB);
        limit.ApplyEntitlements(7, "pro", 200 * MB, 20 * MB, null, isSuspended: false);

        limit.ApplyEntitlements(7, "starter", 100 * MB, 10 * MB, null, isSuspended: true);

        Assert.Equal("pro", limit.PlanCode);
        Assert.Equal(200 * MB, limit.MaxBytes);
        Assert.False(limit.IsSuspended);
        Assert.Equal(7, limit.LastEntitlementsRevision);
    }

    [Fact]
    public void ApplyEntitlements_NewerActiveRevision_LiftsTheSuspension()
    {
        var limit = TenantStorageLimit.Create(Guid.NewGuid(), "starter", maxBytes: 100 * MB, maxFileSizeBytes: 10 * MB);
        limit.ApplyEntitlements(7, "pro", 200 * MB, 20 * MB, null, isSuspended: true);

        limit.ApplyEntitlements(8, "pro", 200 * MB, 20 * MB, null, isSuspended: false);

        Assert.False(limit.IsSuspended);
        Assert.Equal(8, limit.LastEntitlementsRevision);
    }

    [Fact]
    public void ApplyEntitlements_WithoutAPreviousRevision_AppliesAnyRevision()
    {
        var limit = TenantStorageLimit.Create(Guid.NewGuid(), "starter", maxBytes: 100 * MB, maxFileSizeBytes: 10 * MB);

        limit.ApplyEntitlements(1, "pro", 200 * MB, 20 * MB, null, isSuspended: false);

        Assert.Equal("pro", limit.PlanCode);
    }
}
