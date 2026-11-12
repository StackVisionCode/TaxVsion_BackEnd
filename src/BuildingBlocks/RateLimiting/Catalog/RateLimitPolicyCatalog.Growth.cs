namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition GrowthCodesCreate = Define(
        "growth.g.codes_create",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition GrowthCodesRead = Define(
        "growth.f.codes_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition GrowthCodesActivate = Define(
        "growth.g.codes_activate",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition GrowthCodesRevoke = Define(
        "growth.g.codes_revoke",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Migra el limiter nativo "growth-referral-attribution" (particionado por tenant con fallback
    // a IP, 30/min) al sistema tiered — mismo quota, misma semántica de partición solo-Tenant, sin
    // overlay (igual forma que payment_app.m.refund) porque el propósito es anti-enumeración de
    // ReferralCode a nivel tenant, no un límite por usuario individual.
    public static readonly RateLimitPolicyDefinition GrowthReferralAttributionCreate = Define(
        "growth.h.referral_attribution_create",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant,
        [RateLimitPartitionDimension.Tenant],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition GrowthReferralCodeIssue = Define(
        "growth.g.referral_code_issue",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Administración del programa de referidos (PlatformAdmin): crear, versionar la política,
    // activar y suspender. Poca frecuencia por naturaleza; mismo criterio que codes_create/activate.
    public static readonly RateLimitPolicyDefinition GrowthReferralProgramAdmin = Define(
        "growth.g.referral_program_admin",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Lecturas de referidos de la oficina (términos del programa, código propio, invitaciones, premios,
    // historial de envíos). Misma forma que growth.f.codes_read.
    public static readonly RateLimitPolicyDefinition GrowthReferralRead = Define(
        "growth.f.referral_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Validación pública del código de referido (landing, sin sesión): partición por IP; responde un booleano.
    public static readonly RateLimitPolicyDefinition GrowthReferralValidateByIp = Define(
        "growth.a.referral_validate_by_ip",
        RateLimitCategory.A,
        RateLimitPartitionDimension.Ip,
        [],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );

    // Auditoría independiente post-Fase 9: reemplaza el [RateLimitExempt] de
    // InternalCodesController.Quote/ReserveBenefitGift (y la policy nativa "growth-code-quote"
    // asociada, ahora eliminada de Program.cs). El JWT de servicio SÍ trae TenantId
    // (JwtTokenGenerator.GenerateScopedServiceToken lo setea siempre) — la justificación previa
    // ("sin user_id, TieredRateLimitEvaluator no aplicaría") era falsa: el evaluador soporta
    // partición solo-Tenant igual que growth.h.referral_attribution_create.
    // Sin overlay: la primaria ya es por tenant, y un overlay [Tenant] caía en la MISMA clave Redis
    // (cada request contaba dos veces y el límite efectivo quedaba en la mitad). Ídem las dos de abajo.
    public static readonly RateLimitPolicyDefinition GrowthCodesQuote = Define(
        "growth.j.codes_quote",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant,
        [],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket
    );

    // Reemplaza los [RateLimitExempt] de Reserve/Commit/Cancel/Expire/Compensate
    // (InternalCodesController) — mismo M2M ServiceOnly, sin limiter nativo previo (gap
    // preexistente), mismo criterio de partición solo-Tenant que GrowthCodesQuote arriba.
    public static readonly RateLimitPolicyDefinition GrowthCodesReservationManage = Define(
        "growth.j.codes_reservation_manage",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant,
        [],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket
    );

    // Reemplaza los [RateLimitExempt] de Qualify/ConfirmGrant/ConfirmClawback
    // (InternalReferralsController) — mismo criterio que GrowthCodesReservationManage.
    public static readonly RateLimitPolicyDefinition GrowthReferralsManage = Define(
        "growth.j.referrals_manage",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant,
        [],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket
    );
}
