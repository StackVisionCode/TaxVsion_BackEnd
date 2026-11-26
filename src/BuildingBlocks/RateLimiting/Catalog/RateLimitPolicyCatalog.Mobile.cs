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

    // Lectura del personal sobre los viajes de un cliente: mismo cupo que la lectura del portal.
    public static readonly RateLimitPolicyDefinition MobileStaffTripRead = Define(
        "mobile.f.staff_trip_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        300,
        60,
        RateLimitAlgorithm.TokenBucket,
        3000
    );

    // Exportación CSV del registro del año: mismo cupo que las demás exportaciones por usuario.
    public static readonly RateLimitPolicyDefinition MobileStaffTripExport = Define(
        "mobile.h.staff_trip_export",
        RateLimitCategory.H,
        RateLimitPartitionDimension.User,
        [],
        quota: 10,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow
    );
}
