namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Implementación por default de <see cref="IPlanRateLimitReader"/> — siempre "no sé", nunca I/O.
/// Ver <see cref="NullTenantPlanCodeReader"/> para el razonamiento completo (mismo criterio,
/// mismo default hasta Fase 6).
/// </summary>
public sealed class NullPlanRateLimitReader : IPlanRateLimitReader
{
    public Task<PlanRateLimitSnapshot?> GetAsync(
        string planCode,
        RateLimitCategory category,
        CancellationToken ct = default
    ) => Task.FromResult<PlanRateLimitSnapshot?>(null);
}
