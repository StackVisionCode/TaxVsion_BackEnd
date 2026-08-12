namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Compartida por todas las lecturas del catálogo (ItemsController + CategoriesController:
    // List/Get). Lectura ligera paginada, partición (tenant, user) + overlay por tenant.
    public static readonly RateLimitPolicyDefinition CatalogRead = Define(
        "catalog.f.read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por todas las escrituras del catálogo (Create/Update/ChangePrice/SetActive/Delete
    // de ítems y categorías) — escrituras simples sobre una fila.
    public static readonly RateLimitPolicyDefinition CatalogWrite = Define(
        "catalog.g.write",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );
}
