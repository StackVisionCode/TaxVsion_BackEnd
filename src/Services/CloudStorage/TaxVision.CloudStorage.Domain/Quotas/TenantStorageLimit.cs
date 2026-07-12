using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Quotas;

public sealed class TenantStorageLimit : TenantEntity
{
    private TenantStorageLimit() { }

    public string PlanCode { get; private set; } = "starter";
    public long MaxBytes { get; private set; }
    public long UsedBytes { get; private set; }
    public long ReservedBytes { get; private set; }
    public long MaxFileSizeBytes { get; private set; }
    public bool IsSuspended { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static TenantStorageLimit Create(Guid tenantId, string planCode, long maxBytes, long maxFileSizeBytes)
    {
        var limit = new TenantStorageLimit
        {
            Id = tenantId,
            PlanCode = planCode,
            MaxBytes = maxBytes,
            MaxFileSizeBytes = maxFileSizeBytes,
        };
        limit.SetTenant(tenantId);
        return limit;
    }

    public Result Reserve(long bytes)
    {
        if (IsSuspended)
            return Result.Failure(QuotaErrors.Suspended);
        if (bytes <= 0 || bytes > MaxFileSizeBytes)
            return Result.Failure(QuotaErrors.FileTooLarge);
        if (UsedBytes + ReservedBytes + bytes > MaxBytes)
            return Result.Failure(QuotaErrors.Exceeded);
        ReservedBytes += bytes;
        return Result.Success();
    }

    public void Commit(long bytes)
    {
        ReservedBytes = Math.Max(0, ReservedBytes - bytes);
        UsedBytes += bytes;
    }

    public void Release(long bytes) => ReservedBytes = Math.Max(0, ReservedBytes - bytes);

    public void ApplyPlan(string planCode, long maxBytes, long maxFileSizeBytes)
    {
        PlanCode = planCode;
        MaxBytes = maxBytes;
        MaxFileSizeBytes = maxFileSizeBytes;
        IsSuspended = false;
    }

    public void Suspend() => IsSuspended = true;
}

public static class QuotaErrors
{
    public static readonly Error NotProvisioned = new(
        "StorageQuota.NotProvisioned",
        "Storage limits have not been provisioned for this tenant."
    );
    public static readonly Error Suspended = new("StorageQuota.Suspended", "Storage is suspended for this tenant.");
    public static readonly Error FileTooLarge = new(
        "StorageQuota.FileTooLarge",
        "The file exceeds the plan's per-file limit."
    );
    public static readonly Error Exceeded = new("StorageQuota.Exceeded", "The tenant storage quota would be exceeded.");
}
