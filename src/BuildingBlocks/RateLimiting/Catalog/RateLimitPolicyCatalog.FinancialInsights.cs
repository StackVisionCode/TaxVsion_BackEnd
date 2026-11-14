namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Compartida por el detalle y los listados de lotes (staff y portal).
    public static readonly RateLimitPolicyDefinition FinancialImportBatchRead = Define(
        "financial.f.import_batch_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Un lote registra hasta 20 archivos ya escaneados; los bytes los limita CloudStorage. Ventana de 10 min
    // para que un 429 se recupere rápido sin abrir la puerta a ráfagas sostenidas.
    public static readonly RateLimitPolicyDefinition FinancialImportBatchCreate = Define(
        "financial.i.import_batch_create",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 600
    );

    // El cliente desde el portal: la mitad del cupo del staff, mismo techo por oficina.
    public static readonly RateLimitPolicyDefinition FinancialPortalImportBatchCreate = Define(
        "financial.i.portal_import_batch_create",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 30,
        windowSeconds: 600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 600
    );

    // Reintento de un documento fallido antes de la inferencia (no vuelve a cobrar IA).
    public static readonly RateLimitPolicyDefinition FinancialImportDocumentRetry = Define(
        "financial.i.import_document_retry",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 600
    );
}
