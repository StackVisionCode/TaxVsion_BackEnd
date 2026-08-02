namespace BuildingBlocks.RateLimiting;

/// <summary>Réplica de solo lectura de una fila de <c>PlanRateLimits</c> (Subscription) — ver ADR_017.</summary>
public sealed record PlanRateLimitSnapshot(decimal MultiplierOverride, int? HardOverridePerMinute);

/// <summary>
/// Puerto de solo lectura para "¿cuál es el multiplicador/override de este plan para esta
/// categoría?" — la tabla `PlanRateLimits` vive en la base de datos de Subscription; este
/// servicio nunca la consulta directo (ningún servicio consulta la BD de otro). Cada servicio
/// que active el resolver (Fase 6) implementa este puerto sobre una réplica local (cliente M2M
/// + caché, o proyección por evento — mecanismo a definir en Fase 6, fuera de alcance de Fase 2).
/// </summary>
/// <remarks>
/// Mismo requisito de Singleton-safety que <see cref="ITenantPlanCodeReader"/> — ver
/// <c>Customer.Infrastructure.RateLimiting.ScopedPlanRateLimitReader</c>.
/// </remarks>
public interface IPlanRateLimitReader
{
    /// <summary>Devuelve la fila (PlanCode, categoría), o null si no existe/no disponible — el resolver trata null como fail-open.</summary>
    Task<PlanRateLimitSnapshot?> GetAsync(string planCode, RateLimitCategory category, CancellationToken ct = default);
}
