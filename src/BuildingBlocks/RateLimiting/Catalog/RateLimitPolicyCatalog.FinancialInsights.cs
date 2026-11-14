namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition FinancialImportBatchRead = Define(
        "financial.f.import_batch_read", RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant], quota: 300, windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket, overlayQuota: 3000
    );

    // One batch registers up to 20 already scanned files. The byte upload is limited by
    // CloudStorage; this quota limits repeated batch creation and its downstream work.
    public static readonly RateLimitPolicyDefinition FinancialImportBatchCreate = Define(
        "financial.i.import_batch_create",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );
}
