namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition ScribeRender = Define(
        "scribe.j.render",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant,
        [],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket
    );

    // Compartida por Create + AddDraftVersion + PublishVersion de EmailLayout — misma escritura de
    // una fila (mismo criterio que notification.g.layout_manage).
    public static readonly RateLimitPolicyDefinition ScribeLayoutManage = Define(
        "scribe.g.layout_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por Create + AddDraftVersion + PublishVersion de EmailTemplate.
    public static readonly RateLimitPolicyDefinition ScribeTemplateManage = Define(
        "scribe.g.template_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Validate solo parsea/analiza (EmailHtmlSafetyValidator + placeholders) — sin motor de render,
    // a diferencia de Preview. Perfil de lectura liviana pese a ser un endpoint POST con permiso
    // TemplatesRead.
    public static readonly RateLimitPolicyDefinition ScribeTemplateValidate = Define(
        "scribe.f.template_validate",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Preview SÍ invoca IEmailRenderer.PreviewAsync — mismo motor caro que scribe.j.render — pero el
    // actor es un editor humano real (no M2M), así que va particionada Tenant|User (no Tenant-only)
    // con cuota agresiva por-usuario, mismo criterio que customer.h.search para cómputo caro
    // gatillado por un actor humano.
    public static readonly RateLimitPolicyDefinition ScribeTemplatePreview = Define(
        "scribe.j.template_preview",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 60
    );

    // Compartida por List + GetById de EventTemplateMapping.
    public static readonly RateLimitPolicyDefinition ScribeEventMappingRead = Define(
        "scribe.f.event_mapping_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Create + Update + Delete de EventTemplateMapping.
    public static readonly RateLimitPolicyDefinition ScribeEventMappingManage = Define(
        "scribe.g.event_mapping_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );
}
