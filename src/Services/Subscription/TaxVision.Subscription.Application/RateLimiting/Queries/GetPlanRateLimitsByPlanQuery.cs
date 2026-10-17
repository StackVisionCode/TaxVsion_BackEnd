namespace TaxVision.Subscription.Application.RateLimiting.Queries;

/// <summary>CAT-F2.6 — vista admin: filas de rate-limit (multiplicador + hard override) para un plan
/// específico. El GET global M2M <c>internal/plan-rate-limits</c> queda sólo para los otros
/// microservicios.</summary>
public sealed record GetPlanRateLimitsByPlanQuery(string PlanCode);
