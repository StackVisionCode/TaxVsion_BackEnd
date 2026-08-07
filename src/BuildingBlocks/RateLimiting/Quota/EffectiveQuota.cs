namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Cuota efectiva resuelta para un tenant concreto — salida de <see cref="IRateLimitQuotaResolver"/>.
/// </summary>
/// <param name="PermitCount">Cupo final (ya multiplicado/override aplicado) para la ventana.</param>
/// <param name="WindowSeconds">Ventana en segundos — siempre la del <see cref="RateLimitPolicyDefinition"/> original, nunca cambia por plan.</param>
/// <param name="IsFallback">
/// True cuando el resolver no pudo determinar el plan real del tenant (o su multiplicador) y
/// cayó al cupo base sin escalar (invariante §3.3 — fail-open). El middleware de Fase 3 usa
/// este flag para emitir <c>ratelimit.fallback_open_total{policy,reason}</c> (§3.5) — un
/// cupo Standard genuino (tenant real en plan starter) no cuenta como fallback.
/// </param>
/// <param name="OverlayPermitCount">
/// Cupo final de la Capa 2 (overlay tenant), o null si la política no tiene overlay numérico
/// (ver <see cref="RateLimitPolicyDefinition.OverlayQuotaPerMinute"/>). Mismo tratamiento de
/// fallback que <see cref="PermitCount"/>: si el multiplicador no se pudo resolver, este valor
/// es el overlay base sin escalar, no null.
/// </param>
/// <param name="PlanCode">
/// Plan del tenant, o null cuando la categoría no consulta plan (invariante §3.6) o cuando no se
/// pudo resolver. Si el plan sí se resolvió pero faltó su fila en <c>PlanRateLimits</c>, viene
/// poblado con <see cref="IsFallback"/> en true: el plan se conoce aunque la cuota no se escaló.
/// Se usa como tag <c>plan</c> de <c>ratelimit.evaluated_total</c>/<c>blocked_total</c>.
/// </param>
public sealed record EffectiveQuota(
    int PermitCount,
    int WindowSeconds,
    bool IsFallback = false,
    int? OverlayPermitCount = null,
    string? PlanCode = null
);
