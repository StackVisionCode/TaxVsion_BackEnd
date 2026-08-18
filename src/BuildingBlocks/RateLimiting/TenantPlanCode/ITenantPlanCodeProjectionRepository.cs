namespace BuildingBlocks.RateLimiting;

/// <summary>
/// RateLimit Fase 1 — repo genérico sobre la proyección local de cada servicio. Cada servicio
/// declara su propio puerto no-genérico que extiende este (p.ej.
/// <c>ITenantPlanCodeProjectionRepository : ITenantPlanCodeProjectionRepository&lt;TenantPlanCodeProjection&gt;</c>)
/// e implementa el acceso EF en su propia capa Infrastructure — la interfaz en sí no toca ningún
/// DbContext.
/// </summary>
public interface ITenantPlanCodeProjectionRepository<TProjection>
    where TProjection : ITenantPlanCodeProjection
{
    Task<TProjection?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TProjection projection, CancellationToken ct = default);
}

/// <summary>
/// Puerto angosto sobre <c>CachedTenantPlanCodeReader.InvalidateAsync</c> (BuildingBlocks.Infrastructure)
/// — el consumer (Application) no puede referenciar Infrastructure directo.
/// </summary>
public interface ITenantPlanCodeCacheInvalidator
{
    Task InvalidateAsync(Guid tenantId, CancellationToken ct = default);
}
