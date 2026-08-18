namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition PostmasterMessagesRead = Define(
        "postmaster.f.messages_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por GET providers/status (agregado) + GET tenants/{tenantId}/provider
    // (puntual) — mismo perfil de lectura simple sin agregación pesada.
    public static readonly RateLimitPolicyDefinition PostmasterProvidersRead = Define(
        "postmaster.f.providers_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Create/Update/Disable tenant provider + UpsertSystemProvider
    // (PlatformAdmin-only) — las 4 son escrituras simples de una fila de configuración.
    public static readonly RateLimitPolicyDefinition PostmasterProvidersManage = Define(
        "postmaster.g.providers_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition PostmasterSuppressionList = Define(
        "postmaster.f.suppression_list",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Add + Remove de la lista de supresión — misma escritura simple de una fila.
    public static readonly RateLimitPolicyDefinition PostmasterSuppressionManage = Define(
        "postmaster.g.suppression_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition PostmasterDispatch = Define(
        "postmaster.k.dispatch",
        RateLimitCategory.K,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.AccountOrProvider,
        [RateLimitPartitionDimension.AccountOrProvider],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.LeakyBucket
    );
}
