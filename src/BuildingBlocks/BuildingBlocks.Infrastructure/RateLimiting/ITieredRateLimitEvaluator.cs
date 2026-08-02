using BuildingBlocks.RateLimiting;

namespace BuildingBlocks.Infrastructure.RateLimiting;

/// <summary>
/// Evalúa las capas 3 (primaria: tenant+user o user) y 2 (overlay: tenant) de una política para
/// un request concreto — el "TieredRateLimitMiddleware" que nombra Plan_Implementacion_Fases.md
/// §8 Fase 3. Vive fuera de ASP.NET Core (sin ActionContext/HttpContext) a propósito: el filtro
/// de BuildingBlocks.Web (<c>RateLimitAttribute</c>) es la única capa que traduce esto a HTTP.
/// </summary>
public interface ITieredRateLimitEvaluator
{
    Task<RateLimitVerdict> EvaluateAsync(
        RateLimitPolicyDefinition policy,
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );
}
