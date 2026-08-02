namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition DocumentsBrandingRead = Define(
        "documents.f.branding_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition DocumentsBrandingUpsert = Define(
        "documents.g.branding_upsert",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // M2M — Billing pidiendo la generación del PDF de una factura. Mismo shape que
    // scribe.j.render (categoría J, "generación PDF" es un ejemplo textual de J en §4).
    public static readonly RateLimitPolicyDefinition DocumentsInvoiceGenerate = Define(
        "documents.j.invoice_generate",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant,
        [],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket
    );

    // M2M — Auth pidiendo el PDF del recibo de onboarding, pre-tenant (partición cae sobre
    // PlatformTenant.Id, un único bucket compartido por todo el tráfico de onboarding).
    public static readonly RateLimitPolicyDefinition DocumentsOnboardingReceiptGenerate = Define(
        "documents.j.onboarding_receipt_generate",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant,
        [],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket
    );
}
