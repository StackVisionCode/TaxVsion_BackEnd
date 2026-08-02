namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Implementación de referencia de <see cref="IRateLimitQuotaResolver"/> — compone
/// <see cref="ITenantPlanCodeReader"/> + <see cref="IPlanRateLimitReader"/>, ambos puertos puros
/// (sin I/O concreto acá; Fase 6 decide cómo cada servicio los respalda — cliente M2M+caché o
/// proyección local, ver <see cref="IPlanRateLimitReader"/>). No depende de Redis/HTTP/EF —
/// vive en BuildingBlocks core a propósito, testeable con fakes puros (§8 Fase 2).
/// </summary>
public sealed class RateLimitQuotaResolver(
    ITenantPlanCodeReader planCodeReader,
    IPlanRateLimitReader planRateLimitReader
) : IRateLimitQuotaResolver
{
    /// <summary>
    /// Categorías A-E (pre-auth/webhook/público, Bloque I) y P/Q (health/infra, Bloque V) nunca
    /// escalan por plan — invariante §3.6 ("cuota hard-coded solo es válida en categorías
    /// A/B/C/D/E"). Solo Bloque II-IV con tenant (F..O) tienen fila en <c>PlanRateLimits</c>.
    /// </summary>
    private static readonly HashSet<RateLimitCategory> ScalesByPlan =
    [
        RateLimitCategory.F,
        RateLimitCategory.G,
        RateLimitCategory.H,
        RateLimitCategory.I,
        RateLimitCategory.J,
        RateLimitCategory.K,
        RateLimitCategory.L,
        RateLimitCategory.M,
        RateLimitCategory.N,
        RateLimitCategory.O,
    ];

    public async Task<EffectiveQuota> ResolveAsync(
        RateLimitPolicyDefinition policy,
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        if (!ScalesByPlan.Contains(policy.Category))
            return BaseQuota(policy, isFallback: false);

        var planCode = await planCodeReader.GetPlanCodeAsync(tenantId, ct).ConfigureAwait(false);
        if (planCode is null)
            return BaseQuota(policy, isFallback: true);

        var snapshot = await planRateLimitReader.GetAsync(planCode, policy.Category, ct).ConfigureAwait(false);
        if (snapshot is null)
            return BaseQuota(policy, isFallback: true, planCode);

        // Un hard-override reemplaza el cupo primario por completo; no hay override distinto para
        // el overlay (las categorías con hard-override — M/N — no tienen overlay, ver doc de
        // RateLimitPolicyDefinition.OverlayQuotaPerMinute), así que el overlay queda sin escalar acá.
        if (snapshot.HardOverridePerMinute is { } hardOverride)
            return new EffectiveQuota(hardOverride, policy.WindowSeconds, PlanCode: planCode);

        var scaled = Scale(policy.BaseQuotaPerMinute, snapshot.MultiplierOverride);
        var overlay = policy.OverlayQuotaPerMinute is { } overlayBase
            ? Scale(overlayBase, snapshot.MultiplierOverride)
            : (int?)null;

        return new EffectiveQuota(scaled, policy.WindowSeconds, OverlayPermitCount: overlay, PlanCode: planCode);
    }

    private static EffectiveQuota BaseQuota(
        RateLimitPolicyDefinition policy,
        bool isFallback,
        string? planCode = null
    ) => new(policy.BaseQuotaPerMinute, policy.WindowSeconds, isFallback, policy.OverlayQuotaPerMinute, planCode);

    private static int Scale(int baseQuota, decimal multiplier) =>
        Math.Max(1, (int)Math.Round(baseQuota * multiplier, MidpointRounding.AwayFromZero));
}
