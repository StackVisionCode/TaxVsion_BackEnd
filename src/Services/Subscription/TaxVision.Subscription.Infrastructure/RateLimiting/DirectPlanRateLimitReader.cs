using BuildingBlocks.Caching;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Infrastructure.RateLimiting;

/// <summary>
/// Auditoria RateLimit hallazgo #2 — cierra el gap documentado en
/// <c>SubscriptionInfrastructure.DependencyInjection.AddRateLimitTierQuotas</c>/<c>Program.cs</c>:
/// Subscription ES el dueño de la tabla <c>PlanRateLimits</c> y del endpoint M2M que
/// <see cref="BuildingBlocks.Infrastructure.RateLimiting.HttpPlanRateLimitReader"/> llama en el
/// resto de la flota — apuntarse a sí mismo por HTTP+M2M sería un round-trip circular para leer
/// un dato que ya está en el mismo proceso. Mismo shape/cache (5 min, catálogo completo) que
/// <c>HttpPlanRateLimitReader.FetchCatalogAsync</c>, pero leyendo <see cref="IPlanRateLimitRepository"/>
/// directo en vez de HTTP.
/// </summary>
public sealed class DirectPlanRateLimitReader(IPlanRateLimitRepository repository, ICacheService cache)
    : IPlanRateLimitReader
{
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(5);
    private const string CatalogCacheKey = "ratelimit:subscription-plan-rate-limits-catalog";

    public async Task<PlanRateLimitSnapshot?> GetAsync(
        string planCode,
        RateLimitCategory category,
        CancellationToken ct = default
    )
    {
        var catalog = await cache.GetOrCreateAsync(CatalogCacheKey, FetchCatalogAsync, CatalogTtl, ct);
        return catalog.TryGetValue(CatalogKey(planCode, category), out var snapshot) ? snapshot : null;
    }

    private static string CatalogKey(string planCode, RateLimitCategory category) => $"{planCode}:{category}";

    private async Task<IReadOnlyDictionary<string, PlanRateLimitSnapshot>> FetchCatalogAsync(CancellationToken ct)
    {
        var rows = await repository.GetAllAsync(ct);
        var catalog = new Dictionary<string, PlanRateLimitSnapshot>();
        foreach (var row in rows)
        {
            catalog[CatalogKey(row.PlanCode.Value, row.Category)] = new PlanRateLimitSnapshot(
                row.MultiplierOverride,
                row.HardOverridePerMinute
            );
        }
        return catalog;
    }
}

/// <summary>
/// <see cref="IPlanRateLimitReader"/> se registra Singleton (<c>TieredRateLimitingRegistration</c>)
/// pero <see cref="DirectPlanRateLimitReader"/> depende de <see cref="IPlanRateLimitRepository"/>
/// (Scoped, EF) — mismo problema y misma solución que
/// <see cref="BuildingBlocks.Infrastructure.RateLimiting.ScopedPlanRateLimitReader"/>: crea su
/// propio scope por llamada en vez de capturar una dependencia Scoped en un Singleton.
/// </summary>
public sealed class ScopedDirectPlanRateLimitReader(IServiceScopeFactory scopeFactory) : IPlanRateLimitReader
{
    public async Task<PlanRateLimitSnapshot?> GetAsync(
        string planCode,
        RateLimitCategory category,
        CancellationToken ct = default
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var inner = scope.ServiceProvider.GetRequiredService<DirectPlanRateLimitReader>();
        return await inner.GetAsync(planCode, category, ct);
    }
}
