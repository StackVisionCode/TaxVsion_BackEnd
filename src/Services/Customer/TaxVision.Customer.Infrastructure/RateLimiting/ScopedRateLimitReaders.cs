using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace TaxVision.Customer.Infrastructure.RateLimiting;

/// <summary>
/// <c>RateLimitQuotaResolver</c> se registra Singleton pero sus lectores reales dependen de
/// servicios Scoped (DbContext/Redis) — estos wrappers crean su propio scope por llamada para
/// evitar una dependencia cautiva. Crea 2 scopes DI extra por request rate-limiteado (uno por
/// wrapper); se prioriza la independencia de los dos puertos sobre compartir un único scope.
/// </summary>
public sealed class ScopedTenantPlanCodeReader(IServiceScopeFactory scopeFactory) : ITenantPlanCodeReader
{
    public async Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var inner = scope.ServiceProvider.GetRequiredService<CachedTenantPlanCodeReader>();
        return await inner.GetPlanCodeAsync(tenantId, ct);
    }
}

public sealed class ScopedPlanRateLimitReader(IServiceScopeFactory scopeFactory) : IPlanRateLimitReader
{
    public async Task<PlanRateLimitSnapshot?> GetAsync(
        string planCode,
        RateLimitCategory category,
        CancellationToken ct = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var inner = scope.ServiceProvider.GetRequiredService<HttpPlanRateLimitReader>();
        return await inner.GetAsync(planCode, category, ct);
    }
}
