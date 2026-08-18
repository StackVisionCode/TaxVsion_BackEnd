namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition CustomerList = Define(
        "customer.f.list",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Fase 3 — piloto: GET /customers/{id} (un customer puntual) es un endpoint distinto de
    // "customer.f.list" (la búsqueda/listado) pese a compartir categoría F — cada endpoint real
    // audita su propia política (§6.1), no comparte la de otro endpoint "parecido".
    public static readonly RateLimitPolicyDefinition CustomerGetById = Define(
        "customer.f.get",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition CustomerCreate = Define(
        "customer.g.create",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition CustomerSearch = Define(
        "customer.h.search",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );

    public static readonly RateLimitPolicyDefinition CustomerImports = Define(
        "customer.i.imports",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 5,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 20
    );

    public static readonly RateLimitPolicyDefinition CustomerCheckExists = Define(
        "customer.f.check_exists",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por los ~17 endpoints de escritura simple sobre un customer existente (update,
    // addresses/contact-points/relations CRUD, activate/deactivate/archive/reactivate, preparer,
    // portal-invitation, fiscal-profile set) — todos tienen el mismo perfil de costo (una fila, sin
    // agregación), así que comparten cuota de categoría G en vez de una política por endpoint (§5
    // de la guía: "Cuota por categoría, no por endpoint puntual" — distinto del criterio F
    // list-vs-get de Fase 3, donde ambos endpoints SÍ tenían perfiles de costo distintos).
    public static readonly RateLimitPolicyDefinition CustomerWrite = Define(
        "customer.g.write",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // POST /customers/bulk/{action} acepta una lista de CustomerIds — es una escritura masiva
    // sobre múltiples filas en un solo request, no un import/upload de archivo, pero tiene el
    // mismo perfil de costo que la categoría I (cara, debe estar acotada agresivamente).
    public static readonly RateLimitPolicyDefinition CustomerBulkStatusChange = Define(
        "customer.i.bulk_status_change",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 5,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 20
    );

    public static readonly RateLimitPolicyDefinition CustomerImportsGetById = Define(
        "customer.f.imports_get",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition CustomerImportsList = Define(
        "customer.f.imports_list",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition CustomerImportsCancel = Define(
        "customer.g.imports_cancel",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Export CSV/JSON streaming de todas las filas de un import — potencialmente miles de filas,
    // igual perfil que una búsqueda pesada con export (§3 de la guía: "GET con... exports → H").
    public static readonly RateLimitPolicyDefinition CustomerImportsReport = Define(
        "customer.h.imports_report",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );

    public static readonly RateLimitPolicyDefinition CustomerFiscalReveal = Define(
        "customer.n.fiscal_reveal",
        RateLimitCategory.N,
        RateLimitPartitionDimension.User,
        [],
        quota: 5,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow
    );
}
