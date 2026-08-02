namespace TaxVision.Subscription.Application.RateLimiting.Queries;

/// <summary>Catálogo completo de PlanRateLimits — RateLimit Fase 6, consumido por
/// subscriptions/internal/plan-rate-limits.</summary>
public sealed record GetPlanRateLimitsQuery;

public sealed record PlanRateLimitResponse(
    string PlanCode,
    string Category,
    decimal MultiplierOverride,
    int? HardOverridePerMinute
);
