namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition TenantList = Define(
        "tenant.f.list",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por GET logo + GET branding/colors — mismo perfil de costo (lectura de un
    // registro propio del tenant, sin agregación).
    public static readonly RateLimitPolicyDefinition TenantBrandingRead = Define(
        "tenant.f.branding_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition TenantStatusChange = Define(
        "tenant.g.status_change",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por DELETE logo + PUT/DELETE branding/colors — mismas ~3 escrituras simples de
    // una fila (mismo criterio que customer.g.write).
    public static readonly RateLimitPolicyDefinition TenantBrandingManage = Define(
        "tenant.g.branding_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Preserva el comportamiento exacto de la policy ASP.NET Core que reemplaza
    // (Tenant_Service_LogoSupport_Plan.md §10): particionado solo por tenant (no por usuario) —
    // es el cupo de subida de LA marca del tenant, no de cada TenantAdmin/empleado individual que
    // la sube. Sin overlay: la partición primaria ya es por tenant.
    public static readonly RateLimitPolicyDefinition TenantLogoUpload = Define(
        "tenant.i.logo_upload",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant,
        [],
        quota: 10,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow
    );
}
