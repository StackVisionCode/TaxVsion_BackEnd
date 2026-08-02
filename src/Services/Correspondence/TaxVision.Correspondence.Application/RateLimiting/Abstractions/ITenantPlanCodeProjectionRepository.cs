using TaxVision.Correspondence.Domain.RateLimiting;

namespace TaxVision.Correspondence.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
