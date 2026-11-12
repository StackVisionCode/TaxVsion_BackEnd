namespace TaxVision.Subscription.Application.RateLimiting.Queries;

/// <summary>Catálogo completo de PlanRateLimits — RateLimit Fase 6, consumido por
/// internal/plan-rate-limits.</summary>
public sealed record GetPlanRateLimitsQuery;

public sealed record PlanRateLimitResponse(
    string PlanCode,
    string Category,
    decimal MultiplierOverride,
    int? HardOverridePerMinute,
    // D12 (aditivo): true si el plan no tiene fila y el valor sale del perfil por defecto.
    bool FromDefaultProfile = false
);
