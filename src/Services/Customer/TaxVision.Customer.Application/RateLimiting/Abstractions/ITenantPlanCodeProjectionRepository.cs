using TaxVision.Customer.Domain.RateLimiting;

namespace TaxVision.Customer.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
{
    Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default);
}

/// <summary>Puerto angosto sobre CachedTenantPlanCodeReader.InvalidateAsync (BuildingBlocks.Infrastructure)
/// — el consumer (Application) no puede referenciar Infrastructure directo.</summary>
public interface ITenantPlanCodeCacheInvalidator
{
    Task InvalidateAsync(Guid tenantId, CancellationToken ct = default);
}
