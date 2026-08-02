namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Auditoría post-Fase-9 (hallazgo #10) — AuditController.Search acepta filtros reales
    // (aggregateType, aggregateId, rango de fechas) + paginación: búsqueda pesada (H), no lectura
    // simple (F). Era F desde Fase 4.10 por descuido de clasificación, nunca por falta de filtros.
    public static readonly RateLimitPolicyDefinition SubscriptionAuditRead = Define(
        "subscription.h.audit_read",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );

    public static readonly RateLimitPolicyDefinition SubscriptionEntitlementRead = Define(
        "subscription.f.entitlement_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SubscriptionAddOnRead = Define(
        "subscription.f.addon_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SubscriptionSeatRead = Define(
        "subscription.f.seat_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SubscriptionSubscriptionRead = Define(
        "subscription.f.subscription_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SubscriptionAdminRead = Define(
        "subscription.f.admin_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SubscriptionAddOnManage = Define(
        "subscription.g.addon_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition SubscriptionSeatManage = Define(
        "subscription.g.seat_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition SubscriptionPlanChange = Define(
        "subscription.g.plan_change",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition SubscriptionActivate = Define(
        "subscription.g.subscription_activate",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition SubscriptionManage = Define(
        "subscription.g.subscription_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition SubscriptionAdminManage = Define(
        "subscription.g.admin_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );
}
