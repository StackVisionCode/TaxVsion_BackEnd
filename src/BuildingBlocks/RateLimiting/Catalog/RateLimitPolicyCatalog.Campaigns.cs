namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Lecturas ligeras del orquestador de campañas (GET detalle de campaña/run).
    public static readonly RateLimitPolicyDefinition CampaignsGet = Define(
        "campaigns.f.get",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Listados paginados (campañas, runs). Compartida por los GET de lista.
    public static readonly RateLimitPolicyDefinition CampaignsList = Define(
        "campaigns.f.list",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Crear/editar definición de campaña (write ligero).
    public static readonly RateLimitPolicyDefinition CampaignsCreate = Define(
        "campaigns.g.create",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // send-now / disparo: write que produce fan-out por destinatario/canal. Más acotado que un write
    // simple (no por costo de dinero — Campaign no cobra — sino por volumen de mensajería).
    public static readonly RateLimitPolicyDefinition CampaignsSend = Define(
        "campaigns.g.send",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 300
    );
}
