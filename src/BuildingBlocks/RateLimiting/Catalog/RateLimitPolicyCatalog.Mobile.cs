namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition MobileTripRead = Define(
        "mobile.f.trip_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        300,
        60,
        RateLimitAlgorithm.TokenBucket,
        3000
    );
    public static readonly RateLimitPolicyDefinition MobileTripWrite = Define(
        "mobile.g.trip_write",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        60,
        60,
        RateLimitAlgorithm.TokenBucket,
        600
    );
    public static readonly RateLimitPolicyDefinition MobileTripSync = Define(
        "mobile.g.trip_sync",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        120,
        60,
        RateLimitAlgorithm.TokenBucket,
        1200
    );
    public static readonly RateLimitPolicyDefinition MobileVehicleRead = Define(
        "mobile.f.vehicle_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );
    public static readonly RateLimitPolicyDefinition MobileVehicleWrite = Define(
        "mobile.g.vehicle_write",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );
}
