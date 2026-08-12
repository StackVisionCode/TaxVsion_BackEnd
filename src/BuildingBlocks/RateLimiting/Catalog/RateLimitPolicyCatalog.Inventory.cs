namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Compartida por todas las lecturas de inventario (StockController: Get/List/Movements;
    // SuppliersController + ItemSuppliersController: List/Get). Lectura ligera paginada.
    public static readonly RateLimitPolicyDefinition InventoryRead = Define(
        "inventory.f.read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por escrituras de configuración (thresholds, suppliers CRUD, item-supplier links).
    public static readonly RateLimitPolicyDefinition InventoryWrite = Define(
        "inventory.g.write",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Ajuste de existencias (POST stock/{id}/adjust) — escribe el ledger de movimientos; cuota más
    // holgada que un write de config porque la operativa de almacén ajusta seguido (ventas, compras).
    public static readonly RateLimitPolicyDefinition InventoryAdjust = Define(
        "inventory.g.adjust",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 120,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 1200
    );
}
