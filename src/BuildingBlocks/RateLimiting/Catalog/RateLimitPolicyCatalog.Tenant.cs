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
        quota: 20,
        windowSeconds: 600,
        RateLimitAlgorithm.FixedWindow
    );

    // Rama pública anónima de branding (pre-login): resolver la marca por slug y servir el asset por
    // fileId. Categoría D ("público con token"): sin JWT, particiona por el valor de ruta {token} —
    // el slug o el fileId, hasheado por el evaluador. NO usa overlay: el TieredRateLimitEvaluator
    // solo soporta OverlayLayers=[Tenant] y aquí no hay tenant. Partición Ip NO está implementada,
    // así que Token es la única credencial gateable pre-login. La credencial DEBE llegar como
    // parámetro de ruta llamado "token" (RateLimitAttribute.TokenRouteValue) o el filtro hace
    // fail-open (sin límite). Quota generosa: una página de login carga marca + assets varias veces.
    public static readonly RateLimitPolicyDefinition TenantBrandingPublic = Define(
        "tenant.d.branding_public",
        RateLimitCategory.D,
        RateLimitPartitionDimension.Token,
        [],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );
}
