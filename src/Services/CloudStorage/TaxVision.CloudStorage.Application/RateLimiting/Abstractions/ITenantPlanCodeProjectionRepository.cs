using TaxVision.CloudStorage.Domain.RateLimiting;

namespace TaxVision.CloudStorage.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
