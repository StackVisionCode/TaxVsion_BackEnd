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

    // Auditoría independiente post-Fase 9: reemplaza el [RateLimitExempt] de
    // InternalCodesController.Quote/ReserveBenefitGift (y la policy nativa "growth-code-quote"
    // asociada, ahora eliminada de Program.cs). El JWT de servicio SÍ trae TenantId
    // (JwtTokenGenerator.GenerateScopedServiceToken lo setea siempre) — la justificación previa
    // ("sin user_id, TieredRateLimitEvaluator no aplicaría") era falsa: el evaluador soporta
    // partición solo-Tenant igual que growth.h.referral_attribution_create.
    public static readonly RateLimitPolicyDefinition GrowthCodesQuote = Define(
        "growth.j.codes_quote",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Reemplaza los [RateLimitExempt] de Reserve/Commit/Cancel/Expire/Compensate
    // (InternalCodesController) — mismo M2M ServiceOnly, sin limiter nativo previo (gap
    // preexistente), mismo criterio de partición solo-Tenant que GrowthCodesQuote arriba.
    public static readonly RateLimitPolicyDefinition GrowthCodesReservationManage = Define(
        "growth.j.codes_reservation_manage",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Reemplaza los [RateLimitExempt] de Qualify/ConfirmGrant/ConfirmClawback
    // (InternalReferralsController) — mismo criterio que GrowthCodesReservationManage.
    public static readonly RateLimitPolicyDefinition GrowthReferralsManage = Define(
        "growth.j.referrals_manage",
        RateLimitCategory.J,
        RateLimitPartitionDimension.Tenant,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );
}
