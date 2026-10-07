namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Lectura ligera del saldo del monedero (GET /wallet).
    public static readonly RateLimitPolicyDefinition WalletGet = Define(
        "wallet.f.get",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Listado paginado del ledger (GET /wallet/transactions).
    public static readonly RateLimitPolicyDefinition WalletList = Define(
        "wallet.f.list",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Recarga (F2 — POST /wallet/top-ups): write acotado; cada recarga es un cobro real por Stripe.
    public static readonly RateLimitPolicyDefinition WalletTopUp = Define(
        "wallet.g.topup",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 300
    );

    // Autorización del PEP (F4 — POST /internal/wallet/authorizations): interno cross-service, un hit por
    // envío de campaña. Cuota holgada (cada run reserva una vez) pero acotada por tenant.
    public static readonly RateLimitPolicyDefinition WalletAuthorize = Define(
        "wallet.g.authorize",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 120,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 1200
    );
}
