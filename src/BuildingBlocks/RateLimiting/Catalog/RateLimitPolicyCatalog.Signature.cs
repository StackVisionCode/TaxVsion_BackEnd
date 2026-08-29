namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition SignatureSettingsRead = Define(
        "signature.f.settings_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition SignatureSettingsManage = Define(
        "signature.g.settings_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // SignatureAdminController.UpdateConstraints — PlatformAdmin-only (mismo criterio que
    // postmaster.g.providers_manage), escritura simple de una fila de configuración por tenant.
    public static readonly RateLimitPolicyDefinition SignatureAdminConstraintsManage = Define(
        "signature.g.admin_constraints_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // SignatureDocumentsController.Validate — preflight de hasta 25 MB (MIME, integridad
    // estructural, número de páginas, firmas previas), mismo perfil de costo que
    // cloudstorage.i.upload.
    public static readonly RateLimitPolicyDefinition SignatureDocumentValidate = Define(
        "signature.i.document_validate",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 80
    );

    // Compartida por Summary + Timeline + ByCategory (SignatureAnalyticsController) — las 3 leen
    // del mismo snapshot diario poblado por consumers, sin agregación pesada en el request.
    public static readonly RateLimitPolicyDefinition SignatureAnalyticsRead = Define(
        "signature.f.analytics_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por List + GetById (SignatureTemplatesController).
    public static readonly RateLimitPolicyDefinition SignatureTemplateRead = Define(
        "signature.f.template_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Create + UpdateMetadata + UpdateDefaults + AddSlot + RemoveSlot + PlaceField +
    // RemoveField + Publish + Archive (SignatureTemplatesController) — todo el ciclo de vida de
    // edición de una plantilla, misma escritura simple.
    public static readonly RateLimitPolicyDefinition SignatureTemplateManage = Define(
        "signature.g.template_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida deliberadamente por SignatureRequestsController.Create Y
    // SignatureTemplatesController.Instantiate — ambas crean una SignatureRequest nueva, mismo
    // perfil de costo, cupo unificado para que alternar entre las 2 rutas de creación no duplique
    // presupuesto.
    public static readonly RateLimitPolicyDefinition SignatureRequestCreate = Define(
        "signature.g.request_create",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por List + GetById (SignatureRequestsController).
    public static readonly RateLimitPolicyDefinition SignatureRequestRead = Define(
        "signature.f.request_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por AddSigner + RemoveSigner + ReorderSigners + PlaceField + RemoveField + Cancel +
    // ExtendExpiration + ResendSignerInvitation + Set/ClearPractitionerPin + Place/LiftLegalHold +
    // Set/ClearPreparer + SignAsPreparer (SignatureRequestsController) — todas escrituras simples
    // sobre una SignatureRequest existente.
    public static readonly RateLimitPolicyDefinition SignatureRequestManage = Define(
        "signature.g.request_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Política propia pese a compartir cupo G con request_manage — mismo criterio que
    // notification.g.email_send: semánticamente distinguible en métricas aunque el costo HTTP sea
    // igual de barato (202 Accepted, el fan-out real de invitaciones lo hace un worker async).
    public static readonly RateLimitPolicyDefinition SignatureRequestSend = Define(
        "signature.g.request_send",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );
}
